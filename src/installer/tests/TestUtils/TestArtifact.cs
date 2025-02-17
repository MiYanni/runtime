// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Microsoft.DotNet.CoreSetup.Test
{
    public class TestArtifact : IDisposable
    {
        private static readonly Lazy<RepoDirectoriesProvider> _repoDirectoriesProvider =
            new Lazy<RepoDirectoriesProvider>(() => new RepoDirectoriesProvider());

        private static readonly Lazy<bool> _preserveTestRuns = new Lazy<bool>(() =>
            _repoDirectoriesProvider.Value.GetTestContextVariableOrNull("PRESERVE_TEST_RUNS") == "1");

        private static readonly string TestArtifactDirectoryEnvironmentVariable = "TEST_ARTIFACTS";
        private static readonly Lazy<string> _testArtifactsPath = new Lazy<string>(() =>
        {
            return _repoDirectoriesProvider.Value.GetTestContextVariable(TestArtifactDirectoryEnvironmentVariable)
                   ?? Path.Combine(AppContext.BaseDirectory, TestArtifactDirectoryEnvironmentVariable);
        }, isThreadSafe: true);

        public static bool PreserveTestRuns() => _preserveTestRuns.Value;
        public static string TestArtifactsPath => _testArtifactsPath.Value;

        public string Location { get; }
        public string Name { get; }

        protected string DirectoryToDelete { get; init; }

        private readonly List<TestArtifact> _copies = new List<TestArtifact>();

        public TestArtifact(string location)
        {
            Location = location;
            Name = Path.GetFileName(Location);
            DirectoryToDelete = Location;
        }

        protected TestArtifact(TestArtifact source)
        {
            Name = source.Name;
            (Location, DirectoryToDelete) = GetNewTestArtifactPath(source.Name);

            CopyRecursive(source.Location, Location, overwrite: true);

            source._copies.Add(this);
        }

        public static TestArtifact Create(string name)
        {
            var (location, parentPath) = GetNewTestArtifactPath(name);
            return new TestArtifact(location)
            {
                DirectoryToDelete = parentPath
            };
        }

        protected void RegisterCopy(TestArtifact artifact)
        {
            _copies.Add(artifact);
        }

        public virtual void Dispose()
        {
            if (!PreserveTestRuns() && Directory.Exists(DirectoryToDelete))
            {
                try
                {
                    Directory.Delete(DirectoryToDelete, true);
                    Debug.Assert(!Directory.Exists(DirectoryToDelete));

                    // Delete lock file last
                    File.Delete($"{DirectoryToDelete}.lock");
                } catch (Exception e)
                {
                    Console.WriteLine("delete failed" + e);
                }
            }

            foreach (TestArtifact copy in _copies)
            {
                copy.Dispose();
            }

            _copies.Clear();
        }

        protected static (string, string) GetNewTestArtifactPath(string artifactName)
        {
            Exception? lastException = null;
            for (int i = 0; i < 10; i++)
            {
                var parentPath = Path.Combine(TestArtifactsPath, Path.GetRandomFileName());
                // Create a lock file next to the target folder
                var lockPath = parentPath + ".lock";
                var artifactPath = Path.Combine(parentPath, artifactName);
                try
                {
                    File.Open(lockPath, FileMode.CreateNew, FileAccess.Write).Dispose();
                }
                catch (Exception e)
                {
                    // Lock file cannot be created, potential collision
                    lastException = e;
                    continue;
                }
                Directory.CreateDirectory(artifactPath);
                return (artifactPath, parentPath);
            }
            Debug.Assert(lastException != null);
            throw lastException;
        }

        protected static void CopyRecursive(string sourceDirectory, string destinationDirectory, bool overwrite = false)
        {
            FileUtils.EnsureDirectoryExists(destinationDirectory);

            foreach (var dir in Directory.EnumerateDirectories(sourceDirectory))
            {
                CopyRecursive(dir, Path.Combine(destinationDirectory, Path.GetFileName(dir)), overwrite);
            }

            foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            {
                var dest = Path.Combine(destinationDirectory, Path.GetFileName(file));
                if (!File.Exists(dest) || overwrite)
                {
                    // We say overwrite true, because we only get here if the file didn't exist (thus it doesn't matter) or we
                    // wanted to overwrite :)
                    File.Copy(file, dest, overwrite: true);
                }
            }
        }
    }
}
