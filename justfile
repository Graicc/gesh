set windows-shell := ["pwsh","-c"]

alias r := run
alias p := play

run:
	dotnet run

play:
	dotnet run --project ./Projects/Android/gesh.Android.csproj