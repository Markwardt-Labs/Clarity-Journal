#:package Markwardt.ScriptUtilities@0.2.0
#:property TreatWarningsAsErrors=true

// Runs the server locally with a throwaway dev config (access key "dev") under Scripts/Support/Data/,
// rewritten on each start and git-ignored. The desktop client reaches it at localhost and the Android emulator
// at 10.0.2.2. Supports RunDesktop.task and RunMobile.task and is not run directly. File-based app (dotnet
// run) - run from the repo root, e.g. `dotnet run Scripts/Support/DevServer.cs`.

using Markwardt.ScriptUtilities;

string config = "Scripts/Support/Data/config.json";

Directory.CreateDirectory("Scripts/Support/Data");
File.WriteAllText(config, """{ "port": 52681, "key": "dev", "dataFolder": "Scripts/Support/Data" }""");

Script.Log("Dev server: port 52681, access key \"dev\" (localhost from the desktop client, 10.0.2.2 from the emulator).");
(await Script.Run("dotnet", "run", "--project", "Server/Server.csproj", "--", config)).Verify();
