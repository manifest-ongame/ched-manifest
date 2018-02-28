﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using ConcurrentPriorityQueue;
using Ched.Components.Notes;
using Ched.Components.Events;

namespace Ched.Components.Exporter
{
    public class SusExporter : IExporter<SusArgs>
    {
        public string FormatName
        {
            get { return "Seaurchin Score File(sus形式)"; }
        }

        public void Export(string path, ScoreBook book, SusArgs args)
        {
            var notes = book.Score.Notes;
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("This file was generated by Ched {0}.", System.Reflection.Assembly.GetEntryAssembly().GetName().Version.ToString());

                writer.WriteLine("#TITLE \"{0}\"", book.Title);
                writer.WriteLine("#ARTIST \"{0}\"", book.ArtistName);
                writer.WriteLine("#DESIGNER \"{0}\"", book.NotesDesignerName);
                writer.WriteLine("#DIFFICULTY {0}", (int)args.PlayDifficulty + (string.IsNullOrEmpty(args.ExtendedDifficulty) ? "" : ":" + args.ExtendedDifficulty));
                writer.WriteLine("#PLAYLEVEL {0}", args.PlayLevel);
                writer.WriteLine("#SONGID \"{0}\"", args.SongId);
                writer.WriteLine("#WAVE \"{0}\"", args.SoundFileName);
                writer.WriteLine("#WAVEOFFSET {0}", args.SoundOffset);
                writer.WriteLine("#JACKET \"{0}\"", args.JacketFilePath);

                writer.WriteLine();

                writer.WriteLine("#REQUEST \"ticks_per_beat {0}\"", book.Score.TicksPerBeat);

                writer.WriteLine();

                int barTick = book.Score.TicksPerBeat * 4;
                var barIndexCalculator = new BarIndexCalculator(barTick, book.Score.Events.TimeSignatureChangeEvents, args.HasPaddingBar);

                foreach (var item in barIndexCalculator.TimeSignatures)
                {
                    writer.WriteLine("#{0:000}02: {1}", item.StartBarIndex + (args.HasPaddingBar && item.StartBarIndex == 1 ? -1 : 0), 4f * item.TimeSignature.Numerator / item.TimeSignature.Denominator);
                }

                writer.WriteLine();

                for (int i = 0; i < book.Score.Events.BPMChangeEvents.Count; i++)
                {
                    var item = book.Score.Events.BPMChangeEvents[i];
                    var barPos = barIndexCalculator.GetBarPositionFromTick(item.Tick);
                    // if (barData.OffsetTick != 0) // warn
                    writer.WriteLine("#BPM{0:00}:{1}", i + 1, item.BPM);
                    writer.WriteLine("#{0:000}08:{1:00}", barPos.BarIndex + (args.HasPaddingBar && barPos.BarIndex == 1 ? -1 : 0), i + 1);
                }

                writer.WriteLine();

                var speeds = book.Score.Events.HighSpeedChangeEvents.Select(p =>
                {
                    var barPos = barIndexCalculator.GetBarPositionFromTick(p.Tick);
                    return string.Format("{0}'{1}:{2}", barPos.BarIndex, barPos.TickOffset, p.SpeedRatio);
                });
                writer.WriteLine("#TIL00: \"{0}\"", string.Join(", ", speeds));
                writer.WriteLine("#HISPEED 00");

                writer.WriteLine();

                var shortNotes = notes.Taps.Cast<TappableBase>().Select(p => new { Type = '1', Note = p })
                    .Concat(notes.ExTaps.Cast<TappableBase>().Select(p => new { Type = '2', Note = p }))
                    .Concat(notes.Flicks.Cast<TappableBase>().Select(p => new { Type = '3', Note = p }))
                    .Concat(notes.Damages.Cast<TappableBase>().Select(p => new { Type = '4', Note = p }))
                    .Select(p => new
                    {
                        BarPosition = barIndexCalculator.GetBarPositionFromTick(p.Note.Tick),
                        LaneIndex = p.Note.LaneIndex,
                        Width = p.Note.Width,
                        Type = p.Type
                    });

                foreach (var notesInBar in shortNotes.GroupBy(p => p.BarPosition.BarIndex))
                {
                    foreach (var notesInLane in notesInBar.GroupBy(p => p.LaneIndex))
                    {
                        var sig = barIndexCalculator.GetTimeSignatureFromBarIndex(notesInBar.Key);
                        int barLength = barTick * sig.Numerator / sig.Denominator;

                        var offsetList = notesInLane.GroupBy(p => p.BarPosition.TickOffset).Select(p => p.ToList());
                        var separatedNotes = Enumerable.Range(0, offsetList.Max(p => p.Count)).Select(p => offsetList.Where(q => q.Count >= p + 1).Select(q => q[p]));

                        foreach (var dic in separatedNotes.Select(p => p.ToDictionary(q => q.BarPosition.TickOffset, q => q)))
                        {
                            int gcd = dic.Values.Select(p => p.BarPosition.TickOffset).Aggregate(barLength, (p, q) => GetGcd(p, q));
                            writer.Write("#{0:000}1{1}:", notesInBar.Key, notesInLane.Key.ToString("x"));
                            for (int i = 0; i * gcd < barLength; i++)
                            {
                                int tickOffset = i * gcd;
                                writer.Write(dic.ContainsKey(tickOffset) ? dic[tickOffset].Type + ToLaneWidthString(dic[tickOffset].Width) : "00");
                            }
                            writer.WriteLine();
                        }
                    }
                }

                var airs = notes.Airs.Select(p =>
                {
                    string type = "";
                    switch (p.HorizontalDirection)
                    {
                        case HorizontalAirDirection.Center:
                            type = p.VerticalDirection == VerticalAirDirection.Up ? "1" : "2";
                            break;

                        case HorizontalAirDirection.Left:
                            type = p.VerticalDirection == VerticalAirDirection.Up ? "3" : "5";
                            break;

                        case HorizontalAirDirection.Right:
                            type = p.VerticalDirection == VerticalAirDirection.Up ? "4" : "6";
                            break;
                    }

                    return new
                    {
                        BarPosition = barIndexCalculator.GetBarPositionFromTick(p.Tick),
                        LaneIndex = p.LaneIndex,
                        Type = type,
                        Width = p.Width
                    };
                });

                foreach (var airsInBar in airs.GroupBy(p => p.BarPosition.BarIndex))
                {
                    foreach (var airsInLane in airsInBar.GroupBy(p => p.LaneIndex))
                    {
                        var sig = barIndexCalculator.GetTimeSignatureFromBarIndex(airsInBar.Key);
                        int barLength = barTick * sig.Numerator / sig.Denominator;

                        var offsetList = airsInLane.GroupBy(p => p.BarPosition.TickOffset).Select(p => p.ToList());
                        var separatedNotes = Enumerable.Range(0, offsetList.Max(p => p.Count)).Select(p => offsetList.Where(q => q.Count >= p + 1).Select(q => q[p]));
                        foreach (var dic in separatedNotes.Select(p => p.ToDictionary(q => q.BarPosition.TickOffset, q => q)))
                        {
                            int gcd = dic.Values.Select(p => p.BarPosition.TickOffset).Aggregate(barLength, (p, q) => GetGcd(p, q));
                            writer.Write("#{0:000}5{1}:", airsInBar.Key, airsInLane.Key.ToString("x"));
                            for (int i = 0; i * gcd < barLength; i++)
                            {
                                int tickOffset = i * gcd;
                                writer.Write(dic.ContainsKey(tickOffset) ? dic[tickOffset].Type + ToLaneWidthString(dic[tickOffset].Width) : "00");
                            }
                            writer.WriteLine();
                        }
                    }
                }

                var identifier = new IdentifierAllocationManager();

                var holds = book.Score.Notes.Holds
                    .OrderBy(p => p.StartTick)
                    .Select(p => new
                    {
                        Identifier = identifier.Allocate(p.StartTick, p.Duration),
                        StartTick = p.StartTick,
                        EndTick = p.StartTick + p.Duration,
                        Width = p.Width,
                        LaneIndex = p.LaneIndex
                    });

                foreach (var hold in holds)
                {
                    var startBarPosition = barIndexCalculator.GetBarPositionFromTick(hold.StartTick);
                    var endBarPosition = barIndexCalculator.GetBarPositionFromTick(hold.EndTick);
                    if (startBarPosition.BarIndex == endBarPosition.BarIndex)
                    {
                        var sig = barIndexCalculator.GetTimeSignatureFromBarIndex(startBarPosition.BarIndex);
                        int barLength = barTick * sig.Numerator / sig.Denominator;
                        writer.Write("#{0:000}2{1}{2}:", startBarPosition.BarIndex, hold.LaneIndex.ToString("x"), hold.Identifier);
                        int gcd = GetGcd(GetGcd(startBarPosition.TickOffset, endBarPosition.TickOffset), barLength);
                        for (int i = 0; i * gcd < barLength; i++)
                        {
                            int tickOffset = i * gcd;
                            if (startBarPosition.TickOffset == tickOffset) writer.Write("1" + ToLaneWidthString(hold.Width));
                            else if (endBarPosition.TickOffset == tickOffset) writer.Write("2" + ToLaneWidthString(hold.Width));
                            else writer.Write("00");
                        }
                        writer.WriteLine();
                    }
                    else
                    {
                        var startSig = barIndexCalculator.GetTimeSignatureFromBarIndex(startBarPosition.BarIndex);
                        int startBarLength = barTick * startSig.Numerator / startSig.Denominator;
                        writer.Write("#{0:000}2{1}{2}:", startBarPosition.BarIndex, hold.LaneIndex.ToString("x"), hold.Identifier);
                        int gcd = GetGcd(startBarPosition.TickOffset, startBarLength);
                        for (int i = 0; i * gcd < startBarLength; i++)
                        {
                            int tickOffset = i * gcd;
                            if (startBarPosition.TickOffset == tickOffset) writer.Write("1" + ToLaneWidthString(hold.Width));
                            else writer.Write("00");
                        }
                        writer.WriteLine();

                        var endSig = barIndexCalculator.GetTimeSignatureFromBarIndex(endBarPosition.BarIndex);
                        int endBarLength = barTick * endSig.Numerator / endSig.Denominator;
                        writer.Write("#{0:000}2{1}{2}:", endBarPosition.BarIndex, hold.LaneIndex.ToString("x"), hold.Identifier);
                        gcd = GetGcd(endBarPosition.TickOffset, endBarLength);
                        for (int i = 0; i * gcd < endBarLength; i++)
                        {
                            int tickOffset = i * gcd;
                            if (endBarPosition.TickOffset == tickOffset) writer.Write("2" + ToLaneWidthString(hold.Width));
                            else writer.Write("00");
                        }
                        writer.WriteLine();
                    }
                }

                identifier.Clear();

                var slides = notes.Slides
                    .OrderBy(p => p.StartTick)
                    .Select(p => new
                    {
                        Identifier = identifier.Allocate(p.StartTick, p.GetDuration()),
                        Note = p
                    });

                foreach (var slide in slides)
                {
                    var start = new[] { new
                    {
                        TickOffset = 0,
                        BarPosition = barIndexCalculator.GetBarPositionFromTick(slide.Note.StartTick),
                        LaneIndex = slide.Note.StartLaneIndex,
                        Type = "1"
                    } };
                    var steps = slide.Note.StepNotes.OrderBy(p => p.TickOffset).Select(p => new
                    {
                        TickOffset = p.TickOffset,
                        BarPosition = barIndexCalculator.GetBarPositionFromTick(p.Tick),
                        LaneIndex = p.LaneIndex,
                        Type = p.IsVisible ? "3" : "5"
                    }).Take(slide.Note.StepNotes.Count - 1);
                    var endNote = slide.Note.StepNotes.OrderBy(p => p.TickOffset).Last();
                    var end = new[] { new
                    {
                        TickOffset = endNote.TickOffset,
                        BarPosition= barIndexCalculator.GetBarPositionFromTick(endNote.Tick),
                        LaneIndex = endNote.LaneIndex,
                        Type = "2"
                    } };
                    var slideNotes = start.Concat(steps).Concat(end);
                    foreach (var notesInBar in slideNotes.GroupBy(p => p.BarPosition.BarIndex))
                    {
                        foreach (var notesInLane in notesInBar.GroupBy(p => p.LaneIndex))
                        {
                            var sig = barIndexCalculator.GetTimeSignatureFromBarIndex(notesInBar.Key);
                            int barLength = barTick * sig.Numerator / sig.Denominator;
                            int gcd = notesInLane.Select(p => p.BarPosition.TickOffset).Aggregate(barLength, (p, q) => GetGcd(p, q));
                            var dic = notesInLane.ToDictionary(p => p.BarPosition.TickOffset, p => p);
                            writer.Write("#{0:000}3{1}{2}:", notesInBar.Key, notesInLane.Key.ToString("x"), slide.Identifier);
                            for (int i = 0; i * gcd < barLength; i++)
                            {
                                int tickOffset = i * gcd;
                                writer.Write(dic.ContainsKey(tickOffset) ? dic[tickOffset].Type + ToLaneWidthString(slide.Note.Width) : "00");
                            }
                            writer.WriteLine();
                        }
                    }
                }

                identifier.Clear();

                var airActions = notes.AirActions
                    .OrderBy(p => p.StartTick)
                    .Select(p => new
                    {
                        Identifier = identifier.Allocate(p.StartTick, p.GetDuration()),
                        Note = p
                    });

                foreach (var airAction in airActions)
                {
                    var start = new[] { new
                    {
                        TickOffset = 0,
                        BarPosition = barIndexCalculator.GetBarPositionFromTick(airAction.Note.StartTick),
                        Type = "1"
                    } };
                    var actions = airAction.Note.ActionNotes.OrderBy(p => p.Offset).Select(p => new
                    {
                        TickOffset = p.Offset,
                        BarPosition = barIndexCalculator.GetBarPositionFromTick(p.ParentNote.StartTick + p.Offset),
                        Type = "3"
                    }).Take(airAction.Note.ActionNotes.Count - 1);
                    var endNote = airAction.Note.ActionNotes.OrderBy(p => p.Offset).Last();
                    var end = new[] { new
                    {
                        TickOffset = endNote.Offset,
                        BarPosition = barIndexCalculator.GetBarPositionFromTick(airAction.Note.StartTick + endNote.Offset),
                        Type = "2"
                    } };
                    var actionNotes = start.Concat(actions).Concat(end);
                    foreach (var airActionsInBar in actionNotes.GroupBy(p => p.BarPosition.BarIndex))
                    {
                        var sig = barIndexCalculator.GetTimeSignatureFromBarIndex(airActionsInBar.Key);
                        int barLength = barTick * sig.Numerator / sig.Denominator;
                        writer.Write("#{0:000}4{1}{2}:", airActionsInBar.Key, airAction.Note.ParentNote.LaneIndex.ToString("x"), airAction.Identifier);
                        int gcd = airActionsInBar.Select(p => p.BarPosition.TickOffset).Aggregate(barLength, (p, q) => GetGcd(p, q));
                        var dic = airActionsInBar.ToDictionary(p => p.BarPosition.TickOffset, p => p);
                        for (int i = 0; i * gcd < barLength; i++)
                        {
                            int tickOffset = i * gcd;
                            if (dic.ContainsKey(tickOffset)) writer.Write("{0}{1}", dic[tickOffset].Type, airAction.Note.ParentNote.Width);
                            else writer.Write("00");
                        }
                        writer.WriteLine();
                    }
                }
            }
        }

        public static int GetGcd(int a, int b)
        {
            if (a < b) return GetGcd(b, a);
            if (b == 0) return a;
            return GetGcd(b, a % b);
        }

        public static string ToLaneWidthString(int width)
        {
            return width == 16 ? "g" : width.ToString("x");
        }

        public class IdentifierAllocationManager
        {
            private int lastStartTick;
            private Stack<char> IdentifierStack;
            private ConcurrentPriorityQueue<Tuple<int, char>, int> UsedIdentifiers;

            public IdentifierAllocationManager()
            {
                Clear();
            }

            public void Clear()
            {
                lastStartTick = 0;
                IdentifierStack = new Stack<char>(Enumerable.Range(0, 26).Select(p => (char)('A' + p)).Reverse());
                UsedIdentifiers = new ConcurrentPriorityQueue<Tuple<int, char>, int>();
            }

            public char Allocate(int startTick, int duration)
            {
                if (startTick < lastStartTick) throw new InvalidOperationException("startTick must not be less than last called value.");
                while (UsedIdentifiers.Count > 0 && UsedIdentifiers.Peek().Item1 < startTick)
                {
                    IdentifierStack.Push(UsedIdentifiers.Dequeue().Item2);
                }
                char c = IdentifierStack.Pop();
                int endTick = startTick + duration;
                UsedIdentifiers.Enqueue(Tuple.Create(endTick, c), -endTick);
                lastStartTick = startTick;
                return c;
            }
        }

        public class BarIndexCalculator
        {
            private bool hasPaddingBar;
            private int barTick;
            private SortedDictionary<int, TimeSignatureItem> timeSignatures;

            /// <summary>
            /// 時間順にソートされた有効な拍子変更イベントのコレクションを取得します。
            /// </summary>
            public IEnumerable<TimeSignatureItem> TimeSignatures
            {
                get { return timeSignatures.Select(p => p.Value).Reverse(); }
            }

            public BarIndexCalculator(int barTick, IEnumerable<TimeSignatureChangeEvent> events, bool hasPaddingBar)
            {
                this.hasPaddingBar = hasPaddingBar;
                this.barTick = barTick;
                var ordered = events.OrderBy(p => p.Tick).ToList();
                var dic = new SortedDictionary<int, TimeSignatureItem>();
                int pos = 0;
                int barIndex = hasPaddingBar ? 1 : 0;
                for (int i = 0; i < ordered.Count; i++)
                {
                    var item = new TimeSignatureItem()
                    {
                        StartTick = pos,
                        StartBarIndex = barIndex,
                        TimeSignature = ordered[i]
                    };

                    // 時間逆順で追加
                    if (dic.ContainsKey(pos)) dic[-pos] = item;
                    else dic.Add(-pos, item);

                    if (i < ordered.Count - 1)
                    {
                        int barLength = barTick * ordered[i].Numerator / ordered[i].Denominator;
                        int duration = ordered[i + 1].Tick - pos;
                        pos += duration / barLength * barLength;
                        barIndex += duration / barLength;
                    }
                }

                timeSignatures = dic;
            }

            public BarPosition GetBarPositionFromTick(int tick)
            {
                foreach (var item in timeSignatures)
                {
                    if (tick < item.Value.StartTick) continue;
                    var sig = item.Value.TimeSignature;
                    int barLength = barTick * sig.Numerator / sig.Denominator;
                    int tickOffset = tick - item.Value.StartTick;
                    int barOffset = tickOffset / barLength;
                    return new BarPosition()
                    {
                        BarIndex = item.Value.StartBarIndex + barOffset,
                        TickOffset = tickOffset - barOffset * barLength,
                        TimeSignature = item.Value.TimeSignature
                    };
                }

                throw new InvalidOperationException();
            }

            public TimeSignatureChangeEvent GetTimeSignatureFromBarIndex(int barIndex)
            {
                foreach (var item in timeSignatures)
                {
                    if (barIndex < item.Value.StartBarIndex) continue;
                    return item.Value.TimeSignature;
                }

                throw new InvalidOperationException();
            }

            public struct BarPosition
            {
                public int BarIndex { get; set; }
                public int TickOffset { get; set; }
                public TimeSignatureChangeEvent TimeSignature { get; set; }
            }

            public class TimeSignatureItem
            {
                public int StartTick { get; set; }
                public int StartBarIndex { get; set; }
                public TimeSignatureChangeEvent TimeSignature { get; set; }
            }
        }
    }

    [Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptIn)]
    public class SusArgs
    {
        [Newtonsoft.Json.JsonProperty]
        private string playLevel;
        [Newtonsoft.Json.JsonProperty]
        private Difficulty playDificulty;
        [Newtonsoft.Json.JsonProperty]
        private string extendedDifficulty;
        [Newtonsoft.Json.JsonProperty]
        private string songId;
        [Newtonsoft.Json.JsonProperty]
        private string soundFileName;
        [Newtonsoft.Json.JsonProperty]
        private decimal soundOffset;
        [Newtonsoft.Json.JsonProperty]
        private string jacketFilePath;
        [Newtonsoft.Json.JsonProperty]
        private bool hasPaddingBar;

        public string PlayLevel
        {
            get { return playLevel; }
            set { playLevel = value; }
        }

        public Difficulty PlayDifficulty
        {
            get { return playDificulty; }
            set { playDificulty = value; }
        }

        public string ExtendedDifficulty
        {
            get { return extendedDifficulty; }
            set { extendedDifficulty = value; }
        }

        public string SongId
        {
            get { return songId; }
            set { songId = value; }
        }

        public string SoundFileName
        {
            get { return soundFileName; }
            set { soundFileName = value; }
        }

        public decimal SoundOffset
        {
            get { return soundOffset; }
            set { soundOffset = value; }
        }

        public string JacketFilePath
        {
            get { return jacketFilePath; }
            set { jacketFilePath = value; }
        }

        public bool HasPaddingBar
        {
            get { return hasPaddingBar; }
            set { hasPaddingBar = value; }
        }

        public enum Difficulty
        {
            Basic,
            Advanced,
            Expert,
            Master,
            WorldsEnd
        }
    }
}
