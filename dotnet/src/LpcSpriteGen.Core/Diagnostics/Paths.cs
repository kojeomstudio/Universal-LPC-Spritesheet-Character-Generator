// Catalog path resolution — shared by the WPF GUI and the Headless CLI.
// The catalog (sheet_definitions/, palette_definitions/, spritesheets/) lives in the
// LPC submodule repo at tools/lpc-sprite-generator/. When the binaries ship to
// bins/lpc-sprite-generator-dotnet/{headless,wpf}/, the catalog is a SIBLING tree
// (not an ancestor), so we have to look beyond pure ancestor walks.
using System;
using System.Collections.Generic;
using System.IO;

namespace LpcSpriteGen.Core.Diagnostics;

public static class Paths
{
    /// <summary>
    /// Find the LPC submodule repo root (the directory containing sheet_definitions/).
    /// Search order:
    ///   1. LPC_REPO_ROOT env var (always wins if set)
    ///   2. The exe's directory and each ancestor
    ///   3. CWD and each ancestor
    ///   4. Well-known sibling locations relative to each workspace ancestor:
    ///      - tools/lpc-sprite-generator (this workspace's canonical layout)
    ///      - lpc-sprite-generator (when the workspace root IS the submodule repo)
    ///   5. Search C:\workspaces (development tree root) up to 4 levels deep.
    /// </summary>
    public static string ResolveRepoRoot()
    {
        foreach (var candidate in CandidateRoots())
        {
            if (HasCatalog(candidate))
            {
                return candidate;
            }
        }
        return Directory.GetCurrentDirectory();
    }

    private static bool HasCatalog(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        return Directory.Exists(Path.Combine(dir, "sheet_definitions")) &&
               Directory.Exists(Path.Combine(dir, "palette_definitions"));
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string Norm(string? p) => string.IsNullOrEmpty(p) ? "" : Path.GetFullPath(p).TrimEnd('\\', '/');
        bool Yield(string? p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            var n = Norm(p);
            if (seen.Add(n)) { /* unseen */ return true; }
            return false;
        }

        // 1. Explicit env var.
        var env = Environment.GetEnvironmentVariable("LPC_REPO_ROOT");
        if (Yield(env)) yield return env!;

        // 2. exe dir + ancestors, and for each ancestor also probe well-known siblings.
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(exePath)!);
            if (Yield(dir.FullName)) yield return dir.FullName;
            while (dir.Parent != null)
            {
                dir = dir.Parent;
                if (Yield(dir.FullName)) yield return dir.FullName;
                // Sibling probes — canonical workspace layout: <root>/tools/lpc-sprite-generator
                foreach (var sibling in SiblingCandidates(dir.FullName))
                    if (Yield(sibling)) yield return sibling;
            }
        }

        // 3. CWD + ancestors with sibling probes.
        var cwd = new DirectoryInfo(Directory.GetCurrentDirectory());
        if (Yield(cwd.FullName)) yield return cwd.FullName;
        while (cwd.Parent != null)
        {
            cwd = cwd.Parent;
            if (Yield(cwd.FullName)) yield return cwd.FullName;
            foreach (var sibling in SiblingCandidates(cwd.FullName))
                if (Yield(sibling)) yield return sibling;
        }

        // 5. Last-ditch: scan C:\workspaces (dev tree) for any folder with a catalog.
        foreach (var hit in ScanWorkspaces())
            if (Yield(hit)) yield return hit;
    }

    /// <summary>
    /// For a workspace ancestor directory, return canonical sibling locations that
    /// could be the LPC submodule root in this repository's standard layout.
    /// </summary>
    private static IEnumerable<string> SiblingCandidates(string ancestorDir)
    {
        // business/tools/lpc-sprite-generator  (this workspace)
        yield return Path.Combine(ancestorDir, "tools", "lpc-sprite-generator");
        // <root>/lpc-sprite-generator  (when shipped at top level)
        yield return Path.Combine(ancestorDir, "lpc-sprite-generator");
    }

    /// <summary>
    /// Bounded filesystem scan under C:\workspaces for a directory containing the
    /// LPC catalog. Helps when the exe is copied somewhere unusual. Maxdepth 4.
    /// </summary>
    private static IEnumerable<string> ScanWorkspaces()
    {
        foreach (var driveRoot in new[] { @"C:\workspaces", @"D:\workspaces" })
        {
            if (!Directory.Exists(driveRoot)) continue;
            foreach (var hit in ScanForCatalog(driveRoot, maxDepth: 4))
                yield return hit;
        }
    }

    private static IEnumerable<string> ScanForCatalog(string root, int maxDepth)
    {
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            if (HasCatalog(dir)) { yield return dir; yield break; }
            if (depth >= maxDepth) continue;
            DirectoryInfo di;
            try { di = new DirectoryInfo(dir); } catch { continue; }
            DirectoryInfo[] children;
            try { children = di.GetDirectories(); } catch { continue; }
            foreach (var c in children)
            {
                // Skip noise directories
                var n = c.Name;
                if (n.StartsWith('.') || n == "node_modules" || n == "bin" || n == "obj" ||
                    n == ".git" || n == ".godot") continue;
                stack.Push((c.FullName, depth + 1));
            }
        }
    }
}

