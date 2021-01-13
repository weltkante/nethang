rm -fr bin
dotnet publish -c Debug -f netcoreapp3.1 -r ubuntu.20.04-x64 --self-contained false -o bin

