using System;
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

    public class Failure
    {
        public FileInfo file;
        public MediaInfoWrapper mediainfo;
        public InternalMediaInfoResult ffprobe;
    }


    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage (this one is bugged for now): MediaInfoTest -images <directory>");
                Console.WriteLine("Benchmark grabbing media info for all image files in this directory and subdirectories.");
                Console.WriteLine("Usage: MediaInfoTest -av <directory>");
                Console.WriteLine("Benchmark grabbing media info for all audio and video files in this directory and subdirectories.");
                Console.WriteLine("Note: For both ffprobe executable must be in PATH");
                Environment.Exit(1);
            }

            string mode = "av";

            if (args[0] == "-images")
            {
                Console.WriteLine("Filtering for image files.");
                mode = "img";
            }
            else if (args[0] == "-av")
            {
                Console.WriteLine("Filtering for audio and video files.");
                mode = "av";
            }
            else
            {
                Console.WriteLine("Did not understand first argument");
                Environment.Exit(2);
            }

            List<string> extensions = new List<string>();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(string.Format("MediaInfoTest.extensions-{0}.txt", mode)))
            using (StreamReader reader = new StreamReader(stream))
            {
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
            var filteredFiles = files.Where(x => extensions.Contains(x.Extension.ToLowerInvariant())).ToList();
            var skipped_extensions = files.Where(x => !extensions.Contains(x.Extension.ToLowerInvariant())).Select(x => x.Extension).Distinct().ToList();
            var failures = new List<Failure>();
            int total_files = filteredFiles.Count;
            foreach (FileInfo file in filteredFiles)
            {
                if (!stats.ContainsKey(file.Extension.ToLowerInvariant()))
                {
                    stats.Add(file.Extension.ToLowerInvariant(), new Stats());
                }
                if (file.Exists)
                {
                    Console.Write("{1} of {2}: {0}", file.Name, count + 1, total_files);
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
                    stats[file.Extension.ToLowerInvariant()].TimeMediaInfo += inner_sw.Elapsed;
                    count++;
                    stats[file.Extension.ToLowerInvariant()].Count++;
                    Console.Write(" -> MediaInfo Done (v{0}:a{1}:s{2});", info.VideoStreams.Count, info.AudioStreams.Count, info.Subtitles.Count);
                    inner_sw.Restart();
                    var process = new ProcessOptions();
                    // Configure the process using the StartInfo properties.
                    process.FileName = @"ffprobe";
                    process.UseShellExecute = false;
                    process.RedirectStandardOutput = true;
                    process.Arguments = string.Format("-analyzeduration 3000000 -i \"{0}\" -threads 0 -v warning -print_format json -show_streams -show_chapters -show_format", file.FullName.Replace("\"", "\\\""));
                    process.IsHidden = true;
                    process.ErrorDialog = false;
                    process.EnableRaisingEvents = true;
                    var commonProcess = new CommonProcess(process);
                    commonProcess.Start();
                    InternalMediaInfoResult info_ff = new InternalMediaInfoResult();
                    try
                    {
                        info_ff = JsonSerializer.DeserializeFromStream<InternalMediaInfoResult>(commonProcess.StandardOutput.BaseStream);
                    }
                    catch
                    {
                        commonProcess.Kill();
                        Console.Write(" -> Failed...");
                    }
                    commonProcess.WaitForExit(1000);
                    inner_sw.Stop();
                    stats[file.Extension.ToLowerInvariant()].TimeFFProbe += inner_sw.Elapsed;
                    int videostreams = info_ff.streams.Where(x => x.codec_type == "video" && x.disposition["attached_pic"] == "0").Count();
                    int audiostreams = info_ff.streams.Where(x => x.codec_type == "audio").Count();
                    int substreams = info_ff.streams.Where(x => x.codec_type == "subtitle").Count();
                    Console.WriteLine(" -> FFProbe Done (v{0}:a{1}:s{2}).", videostreams, audiostreams, substreams);


                    if (videostreams != info.VideoStreams.Count || audiostreams != info.AudioStreams.Count || substreams != info.Subtitles.Count)
                    {
                        Console.WriteLine("FFProbe and MediaInfo do not agree on the number of streams!", videostreams, audiostreams, substreams);
                        failures.Add(new Failure() { file = file, mediainfo = info, ffprobe = info_ff });
                    }

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
            foreach (var failure in failures)
            {
                Console.WriteLine(string.Format("Failure: Name: {0}; Streams MI: v{1}:a{2}:s{3}; Streams FF: v{4}:a{5}:s{6};", failure.file.FullName,
                     failure.mediainfo.VideoStreams.Count, failure.mediainfo.AudioStreams.Count, failure.mediainfo.Subtitles.Count,
                     failure.ffprobe.streams.Where(x => x.codec_type == "video" && x.disposition["attached_pic"] == "0").Count(),
                     failure.ffprobe.streams.Where(x => x.codec_type == "audio").Count(),
                     failure.ffprobe.streams.Where(x => x.codec_type == "subtitle").Count()
                    ));
            }
        }
    }
}
