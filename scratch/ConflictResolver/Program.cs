using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace ConflictResolver
{
    class Program
    {
        static void Main(string[] args)
        {
            var targetDir = @"d:\Coding\Projects\pocket-mc-dekstop";
            var files = Directory.GetFiles(targetDir, "*.cs", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                if (file.Contains(@"scratch\")) continue;

                var lines = File.ReadAllLines(file).ToList();
                if (!lines.Any(l => l.StartsWith("<<<<<<< HEAD"))) continue;

                Console.WriteLine($"Resolving {file}");
                var newLines = new List<string>();
                var usings = new HashSet<string>();
                bool inConflict = false;

                // Pass 1: Parse normal lines vs conflict markers
                foreach (var line in lines)
                {
                    if (line.StartsWith("<<<<<<< HEAD"))
                    {
                        inConflict = true;
                    }
                    else if (line.StartsWith("======="))
                    {
                        // middle
                    }
                    else if (line.StartsWith(">>>>>>>"))
                    {
                        inConflict = false;
                    }
                    else
                    {
                        if (line.StartsWith("using") && line.EndsWith(";"))
                        {
                            if (!usings.Contains(line))
                            {
                                usings.Add(line);
                                newLines.Add(line);
                            }
                        }
                        else
                        {
                            newLines.Add(line);
                        }
                    }
                }

                File.WriteAllLines(file, newLines);
            }

            Console.WriteLine("Done.");
        }
    }
}
