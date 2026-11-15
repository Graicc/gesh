using System;
using System.IO;
using System.IO.Compression;

namespace gesh;

/// <summary>Minimal PNG encoder for RGBA8 pixels, for thumbnails and paint
/// textures. StereoKit can decode PNGs (Tex.FromMemory) but not encode them.</summary>
static class GPng
{
	/// <param name="rgba">width * height * 4 bytes, top row first.</param>
	public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
	{
		if (rgba.Length != width * height * 4) throw new ArgumentException($"Expected {width * height * 4} bytes, got {rgba.Length}");

		using var png = new MemoryStream();
		png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

		Span<byte> header = stackalloc byte[13];
		WriteBE(header[0..], (uint)width);
		WriteBE(header[4..], (uint)height);
		header[8] = 8;  // bits per channel
		header[9] = 6;  // color type: RGBA
		header[10] = 0; // compression
		header[11] = 0; // filter
		header[12] = 0; // no interlace
		WriteChunk(png, "IHDR", header);

		// Each row starts with a filter byte, 0 = none.
		using var compressed = new MemoryStream();
		using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
		{
			int stride = width * 4;
			for (int y = 0; y < height; y++)
			{
				zlib.WriteByte(0);
				zlib.Write(rgba.Slice(y * stride, stride));
			}
		}
		WriteChunk(png, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
		WriteChunk(png, "IEND", []);
		return png.ToArray();
	}

	/// <summary>Decodes 8 bit RGBA, non-interlaced PNGs, which is what Encode
	/// writes. Other kinds throw, gesh only reads back its own paint.</summary>
	/// <returns>width * height * 4 bytes, top row first.</returns>
	public static (byte[] rgba, int width, int height) Decode(byte[] png)
	{
		int pos = 8;
		int width = 0, height = 0;
		using var idat = new MemoryStream();
		while (pos < png.Length)
		{
			int length = (int)ReadBE(png, pos);
			string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
			int data = pos + 8;
			if (type == "IHDR")
			{
				width = (int)ReadBE(png, data);
				height = (int)ReadBE(png, data + 4);
				if (png[data + 8] != 8 || png[data + 9] != 6 || png[data + 12] != 0) throw new InvalidDataException("Only 8 bit RGBA, non-interlaced PNGs are supported");
			}
			else if (type == "IDAT") idat.Write(png, data, length);
			else if (type == "IEND") break;
			pos = data + length + 4;
		}

		int stride = width * 4;
		byte[] raw = new byte[(stride + 1) * height];
		idat.Position = 0;
		using (var zlib = new ZLibStream(idat, CompressionMode.Decompress)) zlib.ReadExactly(raw);

		byte[] rgba = new byte[stride * height];
		for (int y = 0; y < height; y++)
		{
			byte filter = raw[y * (stride + 1)];
			int src = y * (stride + 1) + 1;
			int dst = y * stride;
			for (int x = 0; x < stride; x++)
			{
				int left = x >= 4 ? rgba[dst + x - 4] : 0;
				int up = y > 0 ? rgba[dst - stride + x] : 0;
				int upLeft = x >= 4 && y > 0 ? rgba[dst - stride + x - 4] : 0;
				int predictor = filter switch
				{
					0 => 0,
					1 => left,
					2 => up,
					3 => (left + up) / 2,
					4 => Paeth(left, up, upLeft),
					_ => throw new InvalidDataException($"Unknown PNG filter {filter}"),
				};
				rgba[dst + x] = (byte)(raw[src + x] + predictor);
			}
		}
		return (rgba, width, height);
	}

	static int Paeth(int a, int b, int c)
	{
		int p = a + b - c;
		int pa = System.Math.Abs(p - a), pb = System.Math.Abs(p - b), pc = System.Math.Abs(p - c);
		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}

	static uint ReadBE(byte[] b, int i) => (uint)(b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3]);

	static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
	{
		Span<byte> b4 = stackalloc byte[4];
		WriteBE(b4, (uint)data.Length);
		s.Write(b4);

		Span<byte> typeBytes = stackalloc byte[4];
		for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
		s.Write(typeBytes);
		s.Write(data);

		uint crc = Crc(Crc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
		WriteBE(b4, crc);
		s.Write(b4);
	}

	static void WriteBE(Span<byte> b, uint v)
	{
		b[0] = (byte)(v >> 24);
		b[1] = (byte)(v >> 16);
		b[2] = (byte)(v >> 8);
		b[3] = (byte)v;
	}

	static readonly uint[] crcTable = MakeCrcTable();

	static uint[] MakeCrcTable()
	{
		var table = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			uint c = n;
			for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
			table[n] = c;
		}
		return table;
	}

	static uint Crc(uint crc, ReadOnlySpan<byte> data)
	{
		foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
		return crc;
	}
}
