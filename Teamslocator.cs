using System.Collections.Generic;
using System;
using System.IO;

namespace TeamsTrayStarter
{
    public static class TeamsLocator
    {
        public static IEnumerable<string> GetLaunchTargets()
        {
            // New Teams: URI is typically best
            yield return "msteams:";
            yield return "ms-teams:";

            // Fallback to WindowsApps alias (New Teams)
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string newTeamsAlias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "ms-teams.exe");
            string classicPerUser = Path.Combine(localAppData, "Microsoft", "Teams", "current", "Teams.exe");
            string classicMachine = Path.Combine(programFilesX86, "Microsoft", "Teams", "current", "Teams.exe");

            if (File.Exists(newTeamsAlias)) yield return newTeamsAlias;
            if (File.Exists(classicPerUser)) yield return classicPerUser;
            if (File.Exists(classicMachine)) yield return classicMachine;
        }
    }
}
