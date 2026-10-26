// Magical Girl ER - JP Voice Always-On patcher
// Makes Japanese character voices play in every game language (EN/CN text stays unchanged).
// Requires the game to be installed on Steam. This tool ships no game assets.
//
// Commands:
//   install   (default) find the game, back up the original DLL, apply the patch
//   uninstall restore the backed-up original DLL
//   status    show whether the patch is currently installed

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    const string GameFolderName = "Magical Girl ER";
    const string ManagedRelative = @"Magical Girl ER_Data\Managed";
    const string DllFileName = "Assembly-CSharp.dll";
    const string BackupFileName = "Assembly-CSharp.dll.orig_backup";

    // SHA-256 of Assembly-CSharp.dll for the Steam build this patch targets (2026-10).
    const string OriginalSha256 = "668d9b79186b065167d940f0e1d5c6df774092dd1e4d1d725092d41ae0594b84";
    const string PatchedSha256 = "dda4864e6798269d32cea14b09de1df2d14cf8e8d2dd42a0dcb8da5dc08d272b";

    static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex.Message);
            return 1;
        }
    }

    static int Run(string[] args)
    {
        string command = "install";
        string pathArg = null;
        foreach (var a in args)
        {
            var lower = a.ToLowerInvariant();
            if (lower == "install" || lower == "uninstall" || lower == "status"
                || lower == "help" || lower == "--help" || lower == "-h")
                command = lower;
            else
                pathArg = a;
        }
        if (command == "help" || command == "--help" || command == "-h") { PrintHelp(); return 0; }

        string gameRoot = FindGameRoot(pathArg);
        if (gameRoot == null)
        {
            Console.WriteLine("[FAIL] Could not find the game 'Magical Girl ER'.");
            Console.WriteLine("       Is it installed on this PC via Steam?");
            Console.WriteLine("       You can also drag the game's install folder (or its .exe) onto this program.");
            return 1;
        }
        string managedDir = Path.Combine(gameRoot, ManagedRelative);
        string dllPath = Path.Combine(managedDir, DllFileName);
        string backupPath = Path.Combine(managedDir, BackupFileName);
        Console.WriteLine("Game: " + gameRoot);

        switch (command)
        {
            case "status": return Status(dllPath, backupPath);
            case "uninstall": return Uninstall(dllPath, backupPath);
            default: return Install(dllPath, backupPath);
        }
    }

    // ---------------------------------------------------------------- install

    static int Install(string dllPath, string backupPath)
    {
        if (IsGameRunning())
        {
            Console.WriteLine("[FAIL] The game is running. Close it first, then try again.");
            return 1;
        }
        string current = Sha256Of(dllPath);
        if (current == PatchedSha256)
        {
            Console.WriteLine("[OK] The patch is already installed. Nothing to do.");
            return 0;
        }
        bool knownOriginal = current == OriginalSha256;
        if (!knownOriginal)
        {
            Console.WriteLine("[WARN] This does not match the game build the patch was made for.");
            Console.WriteLine("       The game may have been updated. Trying anyway - if the code layout");
            Console.WriteLine("       changed, the patch will safely refuse instead of breaking the game.");
        }

        // Keep a copy of the DLL exactly as it was before this patch, for uninstall.
        File.Copy(dllPath, backupPath, overwrite: true);

        // Patch a temp copy first; the real DLL is only touched after the patch succeeded.
        string tmp = dllPath + ".patch_tmp";
        try
        {
            ApplyPatches(dllPath, tmp);
            File.Copy(tmp, dllPath, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }

        if (knownOriginal && Sha256Of(dllPath) != PatchedSha256)
        {
            File.Copy(backupPath, dllPath, overwrite: true);
            Console.WriteLine("[FAIL] Patched DLL failed hash verification; the original was restored.");
            return 1;
        }

        Console.WriteLine("[OK] Patch installed. Japanese voice now plays in every language.");
        Console.WriteLine("     Original file backed up as: " + Path.GetFileName(backupPath));
        Console.WriteLine("     To undo, run: MagicalGirlER-JPVoicePatch.exe uninstall");
        return 0;
    }

    // -------------------------------------------------------------- uninstall

    static int Uninstall(string dllPath, string backupPath)
    {
        if (IsGameRunning())
        {
            Console.WriteLine("[FAIL] The game is running. Close it first, then try again.");
            return 1;
        }
        string current = Sha256Of(dllPath);
        if (current == OriginalSha256)
        {
            Console.WriteLine("[OK] The patch is not installed. Nothing to do.");
            return 0;
        }
        if (!File.Exists(backupPath))
        {
            Console.WriteLine("[FAIL] No backup found at " + backupPath);
            Console.WriteLine("       Easiest fix: in Steam, right-click the game -> Properties -> Installed");
            Console.WriteLine("       Files -> 'Verify integrity of game files'. That restores the original.");
            return 1;
        }
        File.Copy(backupPath, dllPath, overwrite: true);
        Console.WriteLine(Sha256Of(dllPath) == OriginalSha256
            ? "[OK] Original file restored. The patch is uninstalled."
            : "[OK] Backup restored (it was a copy of the game's DLL from before the patch).");
        return 0;
    }

    // ----------------------------------------------------------------- status

    static int Status(string dllPath, string backupPath)
    {
        if (!File.Exists(dllPath)) { Console.WriteLine("[FAIL] Game DLL not found: " + dllPath); return 1; }
        Console.WriteLine("Game DLL:  " + Describe(Sha256Of(dllPath)));
        Console.WriteLine("Backup:    " + (File.Exists(backupPath) ? Describe(Sha256Of(backupPath)) : "not present"));
        return 0;
    }

    static string Describe(string sha) => sha == PatchedSha256 ? "patched (JP voice always-on)"
        : sha == OriginalSha256 ? "original (unpatched)"
        : "unknown version (" + (sha == null ? "unreadable" : sha.Substring(0, 12) + "...") + ")";

    // ----------------------------------------------------------- game locating

    static string FindGameRoot(string pathArg)
    {
        if (pathArg != null)
        {
            string resolved = ResolveGameRootFromPath(pathArg);
            if (resolved != null) return resolved;
            Console.WriteLine("[WARN] Ignoring unrecognized path: " + pathArg);
        }
        foreach (var steam in SteamRoots())
        {
            string candidate = Path.Combine(steam, "steamapps", "common", GameFolderName);
            if (File.Exists(Path.Combine(candidate, ManagedRelative, DllFileName)))
                return candidate;
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (var lib in LibraryPathsFromVdf(vdf))
                {
                    candidate = Path.Combine(lib, "steamapps", "common", GameFolderName);
                    if (File.Exists(Path.Combine(candidate, ManagedRelative, DllFileName)))
                        return candidate;
                }
            }
        }
        return null;
    }

    static string ResolveGameRootFromPath(string path)
    {
        try
        {
            if (File.Exists(path)) path = Path.GetDirectoryName(path);   // exe or DLL dropped in
            if (!Directory.Exists(path)) return null;
            if (File.Exists(Path.Combine(path, ManagedRelative, DllFileName))) return path;    // game root
            if (Path.GetFileName(path).Equals("Managed", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(path, DllFileName)))
                return Directory.GetParent(path).Parent.FullName;                             // .../Managed
            string nested = Path.Combine(path, GameFolderName);                               // steamapps/common
            if (File.Exists(Path.Combine(nested, ManagedRelative, DllFileName))) return nested;
        }
        catch { }
        return null;
    }

    static IEnumerable<string> SteamRoots()
    {
        var roots = new List<string>();
        void Add(string p)
        {
            if (!string.IsNullOrEmpty(p) && Directory.Exists(p) &&
                !roots.Contains(p, StringComparer.OrdinalIgnoreCase))
                roots.Add(p);
        }
        foreach (var key in new[]
                 {
                     Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"),
                     Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"),
                     Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam"),
                 })
        {
            using (key)
            {
                if (key != null)
                    Add((key.GetValue("InstallPath") ?? key.GetValue("SteamPath")) as string);
            }
        }
        Add(@"C:\Program Files (x86)\Steam");
        Add(@"C:\Program Files\Steam");
        return roots;
    }

    static IEnumerable<string> LibraryPathsFromVdf(string vdfPath)
    {
        foreach (Match m in Regex.Matches(File.ReadAllText(vdfPath),
                     "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    static bool IsGameRunning() =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameFolderName)).Length > 0;

    static string Sha256Of(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    // ------------------------------------------------------------ the patches
    //
    // Four neutralizations of the "currentLanguage == Japanese" voice gates,
    // verified against the build hashed above. Every pattern below must match
    // exactly once or the patch aborts without touching the game.

    static void ApplyPatches(string inputPath, string outputPath)
    {
        using (var asm = AssemblyDefinition.ReadAssembly(inputPath))
        {
            var types = asm.MainModule.Types.ToList();
            int patches = 0;

            // 1. GalleryManager::IsJapaneseGalleryLanguage -> always return true
            {
                var m = Method(Type(types, "GalleryManager"), "IsJapaneseGalleryLanguage");
                var ins = m.Body.Instructions;
                if (ins.Count != 4 || ins[0].OpCode != OpCodes.Call || ins[1].OpCode != OpCodes.Ldc_I4_1
                    || ins[2].OpCode != OpCodes.Ceq || ins[3].OpCode != OpCodes.Ret)
                    throw new InvalidOperationException("gallery language check: unexpected code layout");
                m.Body.Instructions.Clear();
                m.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
                m.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                patches++;
            }

            // 2. PatientManager::StartPatientCharacterVoiceWithDelay -> drop language check
            patches += DropVoiceGate(Method(Type(types, "PatientManager"), "StartPatientCharacterVoiceWithDelay"), OpCodes.Ldarg_0);

            // 3. same check inside the delayed-play coroutine state machine
            {
                var pm = Type(types, "PatientManager");
                var nested = pm.NestedTypes.FirstOrDefault(t => t.Name.Contains("PlayPatientCharacterVoiceAfterDelayRoutine"));
                if (nested == null) throw new InvalidOperationException("delayed voice coroutine not found");
                patches += DropVoiceGate(Method(nested, "MoveNext"), OpCodes.Ldloc_1);
            }

            // 4. PatientManager::RefreshLanguageFromSettings -> never stop voices on language change
            {
                var m = Method(Type(types, "PatientManager"), "RefreshLanguageFromSettings");
                int idx = FindGate(m, OpCodes.Ldarg_0);
                if (idx < 0) throw new InvalidOperationException("language-change stop: check not found");
                var ins = m.Body.Instructions;
                if (ins[idx + 3].OpCode != OpCodes.Beq_S && ins[idx + 3].OpCode != OpCodes.Beq)
                    throw new InvalidOperationException("language-change stop: unexpected branch");
                if (!(ins[idx + 4].OpCode == OpCodes.Ldarg_0 && ins[idx + 5].OpCode == OpCodes.Call
                      && ins[idx + 5].Operand is MethodReference mr && mr.Name.StartsWith("Stop")))
                    throw new InvalidOperationException("language-change stop: unexpected code layout");
                ins[idx] = Instruction.Create(OpCodes.Br, (Instruction)ins[idx + 3].Operand);
                ins.RemoveAt(idx + 3); ins.RemoveAt(idx + 2); ins.RemoveAt(idx + 1);
                patches++;
            }

            if (patches != 4) throw new InvalidOperationException("expected 4 patches, applied " + patches);
            asm.Write(outputPath);
        }
    }

    static int DropVoiceGate(MethodDefinition method, OpCode loadOp)
    {
        int idx = FindGate(method, loadOp);
        if (idx < 0) throw new InvalidOperationException(method.Name + ": language gate not found");
        var ins = method.Body.Instructions;
        if (ins[idx + 3].OpCode != OpCodes.Bne_Un_S && ins[idx + 3].OpCode != OpCodes.Bne_Un)
            throw new InvalidOperationException(method.Name + ": unexpected branch");
        var targets = ins.SelectMany(i =>
            i.Operand is Instruction t ? new[] { t } :
            i.Operand is Instruction[] ts ? ts : new Instruction[0]).ToList();
        for (int k = idx; k < idx + 4; k++)
            if (targets.Contains(ins[k]))
                throw new InvalidOperationException(method.Name + ": gate is a branch target");
        ins.RemoveAt(idx + 3); ins.RemoveAt(idx + 2); ins.RemoveAt(idx + 1); ins.RemoveAt(idx);
        return 1;
    }

    // <load> ; ldfld currentLanguage ; ldc.i4.1 ; <conditional branch>
    static int FindGate(MethodDefinition method, OpCode loadOp)
    {
        var ins = method.Body.Instructions;
        for (int i = 0; i + 3 < ins.Count; i++)
        {
            if (ins[i].OpCode != loadOp) continue;
            if (!(ins[i + 1].OpCode == OpCodes.Ldfld && ins[i + 1].Operand is FieldReference fr
                  && fr.Name == "currentLanguage" && fr.DeclaringType.Name == "PatientManager")) continue;
            if (ins[i + 2].OpCode != OpCodes.Ldc_I4_1) continue;
            var b = ins[i + 3].OpCode;
            if (b == OpCodes.Bne_Un_S || b == OpCodes.Beq_S || b == OpCodes.Bne_Un || b == OpCodes.Beq)
                return i;
        }
        return -1;
    }

    static TypeDefinition Type(List<TypeDefinition> types, string name) =>
        types.FirstOrDefault(t => t.Name == name)
        ?? types.SelectMany(t => t.NestedTypes).FirstOrDefault(t => t.Name == name)
        ?? throw new InvalidOperationException("type not found: " + name);

    static MethodDefinition Method(TypeDefinition type, string name) =>
        type.Methods.FirstOrDefault(m => m.Name == name)
        ?? throw new InvalidOperationException(type.Name + "::" + name + " not found");

    static void PrintHelp()
    {
        Console.WriteLine("Magical Girl ER - JP Voice Always-On patch");
        Console.WriteLine();
        Console.WriteLine("Usage: MagicalGirlER-JPVoicePatch.exe [install|uninstall|status] [game-path]");
        Console.WriteLine();
        Console.WriteLine("  install    (default) back up the original file and enable JP voice in all languages");
        Console.WriteLine("  uninstall  restore the backed-up original file");
        Console.WriteLine("  status     show whether the patch is installed");
        Console.WriteLine();
        Console.WriteLine("The game path is auto-detected; drag the game folder or its .exe onto this");
        Console.WriteLine("program to use a non-standard install location.");
    }
}
