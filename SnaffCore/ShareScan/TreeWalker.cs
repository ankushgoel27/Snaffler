﻿using System;
using System.Collections.Generic;
using System.IO;
using Classifiers;

namespace SnaffCore.ShareScan
{
    public class TreeWalker
    {
        public class DirResult
        {
            public bool Snaffle { get; set; }
            public bool ScanDir { get; set; }
            public string DirPath { get; set; }
        }
        private Config.Config Config { get; set; }
        private FileScanner FileScanner { get; set; }

        public TreeWalker(Config.Config config, string shareRoot)
        {
            Config = config;
            if (shareRoot == null)
            {
                config.Mq.Trace("A null made it into TreeWalker. Wtf.");
                return;
            }

            config.Mq.Trace("About to start a TreeWalker on share " + shareRoot);
            FileScanner = new FileScanner();
            WalkTree(shareRoot);
            config.Mq.Trace("Finished TreeWalking share " + shareRoot);
        }

        public void WalkTree(string shareRoot)
        {
            try
            {
                // Walks a tree checking files and generating results as it goes.
                var dirs = new Stack<string>(20);

                if (!Directory.Exists(shareRoot))
                {
                    return;
                }

                dirs.Push(shareRoot);

                while (dirs.Count > 0)
                {
                    var currentDir = dirs.Pop();
                    string[] subDirs;
                    try
                    {
                        subDirs = Directory.GetDirectories(currentDir);
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        Config.Mq.Trace(e.ToString());
                        continue;
                    }
                    catch (DirectoryNotFoundException e)
                    {
                        Config.Mq.Trace(e.Message);
                        continue;
                    }
                    catch (Exception e)
                    {
                        Config.Mq.Trace(e.Message);
                        continue;
                    }

                    string[] files = null;
                    try
                    {
                        files = Directory.GetFiles(currentDir);
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        Config.Mq.Trace(e.Message);
                        continue;
                    }
                    catch (DirectoryNotFoundException e)
                    {
                        Config.Mq.Trace(e.Message);
                        continue;
                    }
                    catch (Exception e)
                    {
                        Config.Mq.Trace(e.Message);
                        continue;
                    }

                    // check if we actually like the files
                    foreach (string file in files)
                    {
                        // TODO: this can be sent to the concurrency shit later to speed up traversal
                        DoFileScanning(file);
                    }

                    // Push the subdirectories onto the stack for traversal if they aren't on any discard-lists etc.
                    foreach (var dirStr in subDirs)
                    {
                        foreach (Classifier dirClassifier in Config.Options.DirClassifiers)
                        {
                            DirResult dirResult = dirClassifier.ClassifyDir(dirStr, Config);
                            // TODO: concurrency uplift: when there is a pooled concurrency queue, just add the dir as a job to the queue
                            if (dirResult.ScanDir) { dirs.Push(dirStr);}

                            if (dirResult.Snaffle)
                            {
                                Config.Mq.DirResult(dirResult);
                            }
                        }

                    }
                }
            }
            catch (Exception e)
            {
                Config.Mq.Error(e.ToString());
            }
        }

        private void DoFileScanning(string file)
        {
            try
            {
                var fileInfo = new FileInfo(file);

                var fileResult = FileScanner.Scan(fileInfo, Config);

                if (fileResult != null)
                {
                    if (fileResult.WhyMatched != FileScanner.MatchReason.NoMatch)
                    {
                        Config.Mq.FileResult(fileResult);
                    }
                }
            }
            catch (FileNotFoundException e)
            {
                // If file was deleted by a separate application
                //  or thread since the call to TraverseTree()
                // then just continue.
                Config.Mq.Trace(e.Message);
                return;
            }
            catch (UnauthorizedAccessException e)
            {
                Config.Mq.Trace(e.Message);
                return;
            }
            catch (PathTooLongException e)
            {
                Config.Mq.Trace(file + " path was too long for me to look at.");
                return;
            }
            catch (Exception e)
            {
                Config.Mq.Trace(e.Message);
                return;
            }
        }
    }
}