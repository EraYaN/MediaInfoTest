﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MediaInfo;
using MediaInfo.Model;
using MediaBrowser.MediaEncoding.Probing;
using ServiceStack.Text;
using MediaBrowser.Model.Diagnostics;
using Emby.Server.Implementations.Diagnostics;
using System.Reflection;

/*
 * Reference:
 * 
Media Info
Video
Title1080p H264
CodecH264
AVCYes
ProfileHigh
Level41
Resolution1920x1080
Aspect ratio16:9
AnamorphicNo
InterlacedNo
Framerate23.976
Bitrate35,378 kbps
Bit depth8 bit
Pixel formatyuv420p
Ref frames1
NAL4
Audio
TitleJapanese PCM_S24BE 2 ch (Default)
LanguageJapanese
CodecPCM_S24BE
Channels2 ch
Bitrate2,304 kbps
Sample rate48,000 Hz
Bit depth24 bit
DefaultYes
Subtitle
TitleUnd (ASS)
CodecASS
DefaultNo
ForcedNo
ExternalYes
Containermkv
PathM:\Music Videos\BABYMETAL\BABYMETAL - LIVE - Legend 1999 & 1997 Apocalypse\2. LEGEND “1997“ SU-METAL Sentaisai at Makuhari Messe Event Hall.mkv
Size20851 MB

 * */

namespace MediaInfoTest
{
    public class Stats
    {
        public int Count;
        public TimeSpan TimeMediaInfo;
        public TimeSpan TimeFFProbe;
        public Stats(int _Count = 0, double _TimeMediaInfoMS = 0, double _TimeFFProbeMS = 0)
        {
            Count = _Count;
            TimeMediaInfo = TimeSpan.FromMilliseconds(_TimeMediaInfoMS);
            TimeFFProbe = TimeSpan.FromMilliseconds(_TimeFFProbeMS);
        }
    }


    class Program
    {
        static void Main(string[] args)
        {
            if(args.Length != 2)
            {
                Console.WriteLine("Usage: MediaInfoTest -images <directory>");
                Console.WriteLine("Benchmark grabbing media info for all image files in this directory and subdirectories.");
                Console.WriteLine("Usage: MediaInfoTest -av <directory>");
                Console.WriteLine("Benchmark grabbing media info for all audio and video files in this directory and subdirectories.");
                Console.WriteLine("Note: For both ffprobe executable must be in PATH");
                Environment.Exit(1);
            }

            string mode = "av";

            if(args[0] == "-images")
            {
                Console.WriteLine("Filtering for image files.");
                mode = "img";
            } else if (args[0] == "-av")
            {
                Console.WriteLine("Filtering for audio and video files.");
                mode = "av";
            } else
            {
                Console.WriteLine("Did not understand first argument");
                Environment.Exit(2);
            }

            List<string> extensions = new List<string>();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(string.Format("MediaInfoTest.extensions-{0}.txt", mode)))
            using (StreamReader reader = new StreamReader(stream)) {
                while (!reader.EndOfStream)
                {
                    var extension = "." + reader.ReadLine();
                    if (!extensions.Contains(extension))
                        extensions.Add(extension);
                }
            }

            var _logger = new ConsoleLogger();
            DirectoryInfo dir = new DirectoryInfo(args[1]);
            if (!dir.Exists)
            {
                Console.WriteLine("Directory does not exist.");
                Environment.Exit(3);
            }

            Stopwatch sw = Stopwatch.StartNew();
            Stopwatch inner_sw = new Stopwatch();
            int count = 0;            
            Dictionary<string, Stats> stats = new Dictionary<string, Stats>();
            var files = dir.GetFiles("*.*", SearchOption.AllDirectories);
            var filteredFiles = files.Where(x => extensions.Contains(x.Extension)).ToList();
            var skipped_extensions = files.Where(x => !extensions.Contains(x.Extension)).Select(x=>x.Extension).Distinct().ToList();
            int total_files = filteredFiles.Count;
            foreach (FileInfo file in filteredFiles)
            {
                if (!stats.ContainsKey(file.Extension))
                {
                    stats.Add(file.Extension, new Stats());
                }
                if (file.Exists)
                {
                    Console.Write("{1} of {2}: {0}", file.Name, count+1, total_files);
                    inner_sw.Restart();
                    MediaInfoWrapper info = new MediaInfoWrapper(file.FullName /*, _logger*/);
                    //foreach (VideoStream stream in info.VideoStreams)
                    //{
                    //    Console.WriteLine("Title: {0} {1}", stream.Resolution, stream.CodecName);
                    //    Console.WriteLine("Codec: {0}", stream.CodecName);
                    //    Console.WriteLine("AVC: {0}", stream.Format);
                    //    Console.WriteLine("Profile: {0}", stream.CodecProfile);
                    //    Console.WriteLine("Resolution: {0}x{1}", stream.Size.Width, stream.Size.Height);
                    //}
                    //foreach (AudioStream stream in info.AudioStreams)
                    //{

                    //}
                    //foreach (SubtitleStream stream in info.Subtitles)
                    //{

                    //}                    
                    inner_sw.Stop();
                    stats[file.Extension].TimeMediaInfo += inner_sw.Elapsed;
                    count++;
                    stats[file.Extension].Count++;
                    Console.Write(" -> MediaInfo Done;");
                    inner_sw.Restart();
                    var process = new ProcessOptions();
                    // Configure the process using the StartInfo properties.
                    process.FileName = @"ffprobe";
                    process.UseShellExecute = false;
                    process.RedirectStandardOutput = true;
                    process.Arguments = string.Format("-analyzeduration 3000000 -i \"{0}\" -threads 0 -v error -print_format json -show_streams -show_chapters -show_format", file.FullName.Replace("\"", "\\\""));
                    process.IsHidden = true;
                    process.ErrorDialog = false;
                    process.EnableRaisingEvents = true;
                    //process.WindowStyle = ProcessWindowStyle.Maximized;
                    var commonProcess = new CommonProcess(process);
                    commonProcess.Start();
                    try
                    {
                        var info_ff = JsonSerializer.DeserializeFromStream<InternalMediaInfoResult>(commonProcess.StandardOutput.BaseStream);
                    }
                    catch
                    {
                        commonProcess.Kill();
                        Console.Write(" -> Failed...");
                    }

                    inner_sw.Stop();
                    stats[file.Extension].TimeFFProbe += inner_sw.Elapsed;
                    Console.WriteLine(" -> FFProbe Done.");
                }
                else
                {
                    Console.WriteLine(string.Format("File {0} not found!", file.Name));
                }

            }
            sw.Stop();
            Console.WriteLine(string.Format("Took {0} for {1} * 2 items ({2:f2} i/s).", sw.Elapsed, count, (count * 2) / sw.Elapsed.TotalSeconds));
            foreach (var kvp in stats)
            {
                Console.WriteLine(string.Format("Extension: {0}, Count: {1}, Time MI: {2}, Speed MI: {4:f2} i/s, Time FF: {3}, Speed FF: {5:f2} i/s.", kvp.Key, kvp.Value.Count,
                    kvp.Value.TimeMediaInfo, kvp.Value.TimeFFProbe,
                    kvp.Value.Count / kvp.Value.TimeMediaInfo.TotalSeconds, kvp.Value.Count / kvp.Value.TimeFFProbe.TotalSeconds));
            }
            foreach (var me in skipped_extensions)
            {
                Console.WriteLine(string.Format("Skipped Extension: {0}", me));
            }
        }
    }
}