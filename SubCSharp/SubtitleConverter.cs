using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;


namespace SubCSharp
{
    public class SubtitleConverter
    {
        //State for WSRT & Webvvt reading
        private enum SState { Empty, Adding, Iterating, Comment, Timestamp };
        private enum SSView { Empty, Timestamp, Content }
        private enum SubFormat { NoMatch, SubViewer, MicroDVD};

        public enum SubtitleNewLineOption {Default, Windows, Unix, MacOLD}

        private static string[] SpaceArray = new string[] { " " }; //Dont want to keep recreating these
        private static string[] NewLineArray = new string[] { "\n" };
        private static string[] CommaArray = new string[] { "," };
        private static string[] CloseSquigArray = new string[] { "}" };

        public SubtitleNewLineOption subtitleNewLineOption = SubtitleNewLineOption.Default;

        //Internal sub format to allow easy conversion
        private class SubtitleEntry
        {
            public DateTime startTime { get; set; }
            public DateTime endTime { get; set; }
            public string content { get; set; }
            public SubtitleEntry(DateTime sTime, DateTime eTime, string text)
            {
                startTime = sTime;
                endTime = eTime;
                content = text;
            }
        }

        List<SubtitleEntry> subTitleLocal;

        //Dictionarty /HashMap whatever
        Dictionary<SubtitleNewLineOption, string> nlDict = new Dictionary<SubtitleNewLineOption, string>();

        public SubtitleConverter() {
            nlDict.Add(SubtitleNewLineOption.MacOLD, "\r");
            nlDict.Add(SubtitleNewLineOption.Unix, "\n");
            nlDict.Add(SubtitleNewLineOption.Windows, "\r\n");
        }
        //-------------------------------------------------------------------------Read Formats---------------//

        /// <summary>
        /// Converts an advanced substation alpha subtitle into the local subtitle format
        /// </summary>
        /// <param name="path">Path to the subtitle to read</param>
        private void ReadASS(string path)
        {
            string subContent;// = File.ReadAllText(path, Encoding.Default);
            using (StreamReader assFileSR = new StreamReader(path)) //Read file to string
            {
                subContent = assFileSR.ReadToEnd();
            }
            subContent = Regex.Replace(subContent, @"\{[^}]*\}", ""); //Remove all additional styling
            using (StringReader assFile = new StringReader(subContent)) 
            {
                string line = "";
                SState state = SState.Empty;
                while ((line = assFile.ReadLine()) != null) //Iterate over string
                {
                    switch(state)
                    {
                        case SState.Empty:
                            if (line.StartsWith("[Events]")) //The row before all dialog
                            {
                                assFile.ReadLine();          //Skip the format
                                state = SState.Iterating;
                            }
                            break;
                        case SState.Iterating:
                            if(line.StartsWith("Dialogue:"))       //As Diaglog starts with this
                            {
                                //Split into 10 Segments
                                //Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
                                //TODO: does it require them all?, or will the magic 10 be replace based on ^
                                string[] splitLine = line.Split(CommaArray, 10 , StringSplitOptions.None);
                                DateTime beginTime = DateTime.ParseExact(splitLine[1], "H:mm:ss.ff", CultureInfo.InvariantCulture);
                                DateTime endTime = DateTime.ParseExact(splitLine[2], "H:mm:ss.ff", CultureInfo.InvariantCulture);
                                string text = splitLine[9].Replace("\\N", "\n");//Replace \N with actual newlines
                                subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, text));
                            }
                            break;
                    }
                }
            }
            //Since ass/ssa can be in any order we must do this;
            //It shouldn't mess up already ordered for the next part
            subTitleLocal = subTitleLocal.OrderBy(o => o.startTime).ToList();
            JoinSameStart();
        }
        /// <summary>
        /// Converts a dfxp subtitle into the local subtitle format
        /// </summary>
        /// <param name="path">The path to the dfxp to convert</param>
        private void ReadDFXP(string path)
        {
            using (XmlTextReader reader = new XmlTextReader(path))
            {
                reader.Namespaces = false;// Namespaces are annoying, screw them.
                while (reader.ReadToFollowing("p")) //Read all p nodes
                {
                    DateTime beginTime;
                    DateTime endTime;
                    string begin = reader.GetAttribute("begin");
                    bool beginSuc = DateTime.TryParse(begin, out beginTime);
                    if (!beginSuc) //If that failed parse it differently
                    {
                        beginTime = ParseTimeMetric(begin);
                    }

                    string end = reader.GetAttribute("end");
                    if (end != null)
                    {
                        bool endSuc = DateTime.TryParse(end, out endTime);

                        if (!endSuc) //If that failed parse it differently
                        {
                            endTime = ParseTimeMetric(end);
                        }
                    }
                    else //The subtitle uses the start dur format
                    {
                        end = reader.GetAttribute("dur");
                        TimeSpan ts;
                        bool endSuc = TimeSpan.TryParse(end, out ts);
                        if (!endSuc) //If that failed parse it differently
                        {
                            ts = ParseTimeMetricAsTimeSpan(end);
                        }
                        endTime = beginTime.Add(ts);
                    }
                    string text = reader.ReadInnerXml();
                    text = Regex.Replace(text, "(\r\n?|\n) *", ""); //Debeutify xml node
                    text = text.Replace("<br /><br />", "\n").Replace("<br />", "\n"); //Depends on the format remove all
                    subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, text));
                }
            }
            JoinSameStart();
        }
        /// <summary>
        /// Reads a MicroDVD subtitle file
        /// </summary>
        /// <param name="path">Path to the subtitle to read</param>
        private void ReadMicroDVD(string path)
        {
            //\d+\.\d+
            DateTime startTime;
            DateTime endTime;
            Regex regexSplit = new Regex(@"(?<=\})");
            Regex removeMeta = new Regex( @"\{[^}]*\}");
            string raw = File.ReadAllText(path,Encoding.Default);
            float fps;
            using (StringReader mDVD = new StringReader(raw))
            {
                //First case
                string line = mDVD.ReadLine(); //First case options for framerate of video need to be handled
                string beginFrameStr;          //String of frames for starttime
                string endFrameStr;             //String of frames for endtime
                string[] splitFirst = regexSplit.Split(line, 3);
                string contentFirst = splitFirst[2].Replace("[","").Replace("]","").Replace(",",".");

                if (!float.TryParse(contentFirst, out fps))
                {
                    fps = 23.976f;
                    beginFrameStr = splitFirst[0].Substring(1, splitFirst[0].Length - 2);
                    endFrameStr = splitFirst[1].Substring(1, splitFirst[1].Length - 2);
                    startTime = framesToDateTime(int.Parse(beginFrameStr), fps);
                    endTime = framesToDateTime(int.Parse(endFrameStr), fps);
                    string content = removeMeta.Replace(splitFirst[2], "").Replace("|", "\n");
                    subTitleLocal.Add(new SubtitleEntry(startTime, endTime, content));
                }
                while ((line = mDVD.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line == "") continue;
                    string[] split = regexSplit.Split(line,3);
                    //Remove start and end {}
                    beginFrameStr = split[0].Substring(1,split[0].Length-2);
                    endFrameStr = split[1].Substring(1,split[1].Length-2);
                    //Parse into datetime
                    startTime = framesToDateTime(int.Parse(beginFrameStr), fps);
                    endTime = framesToDateTime(int.Parse(endFrameStr), fps);
                    //Remove markup and add newlines
                    string content = removeMeta.Replace(split[2],"").Replace("|","\n");
                    subTitleLocal.Add(new SubtitleEntry(startTime, endTime, content));
                }
            }
        }

        /// <summary>
        /// Handles analysizing the type of .sub format (microdvd, subviewer)
        /// </summary>
        /// <param name="path">Path to the subtitle to read</param>
        private void ReadSub(string path)
        {
            string head;
            SubFormat format = SubFormat.NoMatch;
            using (StreamReader file = new StreamReader(path))
            {
                
                head = file.ReadLine();
                if (head.StartsWith("[")) format = SubFormat.SubViewer;
                else if (head.StartsWith("{")) format = SubFormat.MicroDVD;
            }
            switch(format)
            {
                case(SubFormat.SubViewer):
                    ReadSubViewer(path);
                    break;
                case(SubFormat.MicroDVD):
                    ReadMicroDVD(path);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Reads Subviewer 2.0 format to local format
        /// </summary>
        /// <param name="path">Path to the subview file</param>
        private void ReadSubViewer(string path)
        {
            string raw = File.ReadAllText(path, Encoding.Default);
            raw = Regex.Replace(raw, @"\{[^}]*\}", "");
            //raw = raw.Replace("[br]", "\n"); //Replace newlines
            SSView state = SSView.Empty;
            using (StringReader sbv = new StringReader(raw))
            {
                DateTime sTime = new DateTime();
                DateTime eTime = new DateTime();
                string line;
                string tsMatch = @"^\d\d:\d\d";
                while((line = sbv.ReadLine()) != null)
                {
                    if (line.Trim().Equals("")) continue;//Blank lines
                    switch(state)
                    {
                        case SSView.Empty:
                            if(Regex.IsMatch(line,tsMatch)) //till we find the timestamp
                                goto case SSView.Timestamp;
                            break;
                        case SSView.Timestamp:
                            string[] split = line.Split(CommaArray,StringSplitOptions.None);
                            sTime = DateTime.ParseExact(split[0], "HH:mm:ss.ff", CultureInfo.InvariantCulture);
                            eTime = DateTime.ParseExact(split[1], "HH:mm:ss.ff", CultureInfo.InvariantCulture);
                            state = SSView.Content;
                            break;
                        case SSView.Content:
                            subTitleLocal.Add(new SubtitleEntry(sTime,eTime,line.Replace("[br]","\n")));
                            sbv.ReadLine(); //Blank line always after content
                            state = SSView.Timestamp;
                            break;
                    }
                }
            }
        }
        /// <summary>
        /// Converts a srt subtitle into the local subtitle format
        /// </summary>
        /// <param name="path">Input path for the subtitle</param>
        private void ReadSRT(string path)
        {
            string raw = File.ReadAllText(path, Encoding.Default);
            raw = Regex.Replace(raw, @"<[^>]*>", "");
            string[] split = Regex.Split(raw, @"\n\n[0-9]+\n"); //Each etnry can be separted like this, a subtitle cannot contain a blank line followed by a line containing only a decimal number appartently
            //First case is a bit different as it has an extra row or maybe junk
            string case1 = split[0].TrimStart();
            string[] splitc1 = case1.Split(new string[] { "\n" }, StringSplitOptions.None);

            string[] time = Regex.Split(splitc1[1], " *--> *");           //May or may not have a space or more than 2 dashes

            DateTime beginTime;
            DateTime endTime;

            beginTime = DateTime.ParseExact(time[0], "HH:mm:ss,fff", CultureInfo.InvariantCulture);
            endTime = DateTime.ParseExact(time[1], "HH:mm:ss,fff", CultureInfo.InvariantCulture);

            string tmp = splitc1[2];
            foreach (string text in splitc1.Skip(3))
            {
                tmp = tmp + "\n" + text;
            }

            subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, tmp));
            //Main loop
            foreach (string sub in split.Skip(1))
            {
                string[] splitc2 = sub.Split(new string[] { "\n" }, StringSplitOptions.None);

                string[] time2 = Regex.Split(splitc2[0], " *--> *");           //May or may not have a space or more than 2 dashes
                DateTime beginTime2;
                DateTime endTime2;
                beginTime2 = DateTime.ParseExact(time2[0], "HH:mm:ss,fff", CultureInfo.InvariantCulture);
                endTime2 = DateTime.ParseExact(time2[1], "HH:mm:ss,fff", CultureInfo.InvariantCulture);

                string tmp2 = splitc2[1].TrimEnd();
                foreach (string text in splitc2.Skip(2))
                {
                    tmp2 = tmp2 + "\n" + text.TrimEnd();
                }

                subTitleLocal.Add(new SubtitleEntry(beginTime2, endTime2, tmp2));
            }
        }
        /// <summary>
        /// Reads a WebVTT to the applications local format
        /// </summary>
        /// <param name="path">The path to the subtitle to convert</param>
        private void ReadWebVTT(string path)
        {
            string raw = File.ReadAllText(path, Encoding.Default);
            raw = raw.Replace("\r\n", "\n");    //Replace Windows format
            raw = raw.Replace("\r", "\n");      //Replace old Mac format (it's in the specs to do so)
            raw = raw.Trim();
            raw = Regex.Replace(raw, @"<v(.*? )(.*?)>", "$2: ");    //Replace voice tags with a "Name: "
            raw = Regex.Replace(raw, @"<[^>]*>", "");                //Remove all anotations

            var splited = raw.Split(NewLineArray, StringSplitOptions.None).ToList();
            SState ss = SState.Empty; //Current state
            if (!splited[0].StartsWith("WEBVTT")) return; //Not a valid WebVTT
            DateTime beginTime = new DateTime();
            DateTime endTime = new DateTime();
            string textContent = "";
            foreach (string line in splited.Skip(1)) //Iterate line by line
            {
                switch (ss)
                {
                    case (SState.Empty):
                        string linetrim = line.TrimEnd();
                        if (linetrim.Equals("")) continue;                            //Run past newlines
                        //Style is only allowed to appear before all cues, hence a separate test, 
                        //unsure if you can have style and a :: value on same line, so test both  anyway
                        if (subTitleLocal.Count == 0 && linetrim.Equals("STYLE") || linetrim.StartsWith("STYLE "))
                        {
                            ss = SState.Comment;                                //We want to skip like a note;
                            break;
                        }
                        //WEBVTTComment, or region values we'll just skip
                        if (linetrim.Equals("NOTE") || linetrim.StartsWith("NOTE ") || linetrim.Equals("REGION"))
                        {
                            ss = SState.Comment;
                            break;
                        }
                        if (linetrim.Contains("-->")) goto case (SState.Timestamp); //As we dont care for Queue ID, test only for timestamp
                        break; //Must be junk data, spec says to abort, but lets keep going anhyway

                    case (SState.Timestamp):
                        //Split and parse the timestamp 
                        string[] time = Regex.Split(line, " *--> *");
                        //Parse the time, can only be one of 2 option, so try this one first
                        bool tryBegin = DateTime.TryParseExact(time[0], "HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out beginTime);
                        string[] endTimeSplit = time[1].Split(SpaceArray, StringSplitOptions.None); //Timestamp might contain info after it, so remove it
                        bool tryEnd = DateTime.TryParseExact(endTimeSplit[0], "HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out endTime);
                        //If something went wrong, parse it differnetly;
                        if (!tryBegin) DateTime.TryParseExact(time[0], "mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out beginTime);
                        if (!tryEnd) DateTime.TryParseExact(endTimeSplit[0], "mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out endTime);
                        ss = SState.Iterating;
                        break;

                    case (SState.Iterating):
                        if (line.Equals("")) //Come to the end of the cue block so add it to the sub
                        {
                            string cleanedString = textContent.TrimEnd(); //Remove the additional newline we added
                            subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, cleanedString));
                            textContent = "";                             //Cleanup
                            ss = SState.Empty;
                        }
                        else textContent += line + "\n"; //Otherwise just add to the cue content;
                        break;

                    case (SState.Comment): //We dont want notes so lets go here
                        string linetrimc = line.TrimEnd();
                        if (linetrimc.Equals("")) ss = SState.Empty;//Reached the end of the comment/style/region;
                        break;
                }
            }
            if (ss == SState.Iterating) //End of file, add last if we were still going
            {
                string cleanedString = textContent.TrimEnd(); //Remove the additional newline we added
                subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, cleanedString));
            }

        }
        /// <summary>
        /// Converts a wsrt subtitle into the local subtitle format
        /// Old, Use ReadWSRT2 instead
        /// </summary>
        /// <param name="path">Input path for the subtitle</param>
        private void ReadWSRT(string path)
        {
            //Very similar to ReadSRT, however removes additions such as cues settings
            // Neeed to support addition info at http://annodex.net/~silvia/tmp/WebSRT/#slide1
            string raw = File.ReadAllText(path, Encoding.Default);
            raw = raw.Replace("\r\n", "\n");
            string[] split = Regex.Split(raw, @"\n\n[0-9]+\n"); //Each etnry can be separted like this
            //First case is a bit different as it has an extra row or maybe junk
            string case1 = split[0].TrimStart();
            string[] splitc1 = case1.Split(NewLineArray, StringSplitOptions.None);

            string[] time = Regex.Split(splitc1[1], " *--> *");           //May or may not have a space or more than 2 dashes

            DateTime beginTime;
            DateTime endTime;

            beginTime = DateTime.ParseExact(time[0], "HH:mm:ss.fff", CultureInfo.InvariantCulture);
            endTime = DateTime.ParseExact(time[1], "HH:mm:ss.fff", CultureInfo.InvariantCulture);

            string tmp = splitc1[2];
            foreach (string text in splitc1.Skip(3))
            {
                tmp = tmp + "\n" + text;
            }
            tmp = Regex.Replace(tmp, @"</*[0-9]+>", "");
            subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, tmp));
            //Main loop
            foreach (string sub in split.Skip(1))
            {
                string[] splitc2 = sub.Split(NewLineArray, StringSplitOptions.None);

                string[] time2 = Regex.Split(splitc2[0], " *--> *");           //May or may not have a space or more than 2 dashes
                DateTime beginTime2;
                DateTime endTime2;
                beginTime2 = DateTime.ParseExact(time[0].Substring(0, 12), "HH:mm:ss.fff", CultureInfo.InvariantCulture);
                endTime2 = DateTime.ParseExact(time[1].Substring(0, 12), "HH:mm:ss.fff", CultureInfo.InvariantCulture);

                string tmp2 = splitc2[1].TrimEnd();
                foreach (string text in splitc2.Skip(2))
                {
                    tmp2 = tmp2 + "\n" + text.TrimEnd();
                }
                tmp2 = Regex.Replace(tmp2, @"</*[0-9]+>", "");
                subTitleLocal.Add(new SubtitleEntry(beginTime2, endTime2, tmp2));
            }

        }
        /// <summary>
        /// Converts a wsrt subtitle into the local subtitle format
        /// </summary>
        /// <param name="path">Path to the WSRT file</param>
        private void ReadWSRT2(string path)
        {

            string raw = File.ReadAllText(path, Encoding.Default);
            raw = raw.Replace("\r\n", "\n");
            raw = raw.Trim();
            var splited = raw.Split(NewLineArray, StringSplitOptions.None).ToList();
            DateTime beginTime = new DateTime();
            DateTime endTime = new DateTime();
            string previous = "<blankstring>"; //Since need to handle first time option and can
            string subContent = "";
            SState ss = SState.Empty;
            string cleanedString = "";
            foreach (string line in splited)
            {
                switch (ss)
                {
                    case (SState.Empty): //First time
                        if (line.Contains("-->"))
                        {
                            string[] time = Regex.Split(line, " *--> *");
                            DateTime.TryParse(time[0], out beginTime);
                            DateTime.TryParse(time[1].Split(SpaceArray, StringSplitOptions.None)[0], out endTime);
                            //beginTime = DateTime.ParseExact(time[0].Substring(0, 12), "HH:mm:ss.fff", CultureInfo.InvariantCulture);
                            //endTime = DateTime.ParseExact(time[1].Substring(0, 12), "HH:mm:ss.fff", CultureInfo.InvariantCulture);
                            ss = SState.Adding;
                        }
                        break;
                    case (SState.Adding):
                        if (line.Contains("-->"))
                        {
                            //Add
                            cleanedString = subContent.TrimEnd();
                            cleanedString = Regex.Replace(cleanedString, @"</*[0-9]+>", "");
                            subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, cleanedString));
                            //Cleanup for new
                            subContent = "";
                            previous = "<blankstring>";
                            //Set date
                            string[] time = Regex.Split(line, " *--> *");
                            DateTime.TryParse(time[0], out beginTime);
                            DateTime.TryParse(time[1].Split(SpaceArray, StringSplitOptions.None)[0], out endTime);
                            //beginTime = DateTime.ParseExact(time[0].Substring(0, 12), "HH:mm:ss.fff", CultureInfo.InvariantCulture);
                            //endTime = DateTime.ParseExact(time[1].Substring(0, 12), "HH:mm:ss.fff", CultureInfo.InvariantCulture);

                        }
                        else if (previous.Equals("<blankstring>"))
                        {
                            previous = line;
                        }
                        else
                        {
                            subContent += previous + "\n";
                            previous = line;
                        }
                        break;
                }
            }
            //Add stragggler
            cleanedString = subContent.TrimEnd();
            cleanedString = Regex.Replace(cleanedString, @"</*[0-9]+>", "");
            subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, cleanedString));

        }
        //-------------------------------------------------------------------------Write Formats---------------//
        /// <summary>
        /// Writes the current subtitle stored to the advanced subtitle station alpha
        /// </summary>
        /// <param name="path">The location to save to</param>
        private void WriteASS(string path)
        {
            string nlASS = "\n";
            if(subtitleNewLineOption != SubtitleNewLineOption.Default)
            {
                nlASS = nlDict[subtitleNewLineOption];
            }
            string head = "[Script Info]"      + nlASS +
                          "Title: <untitled>"  + nlASS +
                          "ScriptType: v4.00+" + nlASS +
                          "Collisions: Normal" + nlASS +
                          "PlayDepth: 0" + nlASS + nlASS;

            string styles = "[v4+ Styles]" + nlASS +
                            "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding" + nlASS +
                            "Style: Default,Arial,20,&H00FFFFFF,&H000080FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,2,2,2,10,10,20,0\n\n";
            string events = "[Events]" + nlASS +
                           "Format: Layer, Start, End, Style, Actor, MarginL, MarginR, MarginV, Effect, Text" + nlASS;
            StringBuilder builder = new StringBuilder();
            builder.Append(head);
            builder.Append(styles);
            builder.Append(events);
            foreach(SubtitleEntry entry in subTitleLocal)
            {
                string startTime = entry.startTime.ToString("H:mm:ss.ff");
                string endTime = entry.endTime.ToString("H:mm:ss.ff");
                builder.Append(string.Format("Dialogue: 0,{0},{1},Default,,0,0,0,,{2}" + nlASS, startTime, endTime, entry.content.Replace("\n", "\\N")));
            }
            File.WriteAllText(path, builder.ToString());
        }

        /// <summary>
        /// Writes the current subtitle stored as a DFXP
        /// </summary>
        /// <param name="path">Output path for subtitle</param>
        private void WriteDFXP(string path)
        {
            string nlDFXP = "\n";
            if (subtitleNewLineOption != SubtitleNewLineOption.Default)
            {
                nlDFXP = nlDict[subtitleNewLineOption];
            }
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ", //Two spaces
                NewLineChars = nlDFXP
            };
            using (var fs = new FileStream(path, FileMode.Create))
            {
                using (XmlWriter writer = XmlWriter.Create(fs,settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("tt", "http://www.w3.org/ns/ttml");
                    writer.WriteStartElement("body");
                    writer.WriteStartElement("div");
                    writer.WriteAttributeString("xml", "lang", null, "en");
                    int i = 0;
                    foreach (SubtitleEntry entry in subTitleLocal)
                    {
                        i++;
                        string sTime = entry.startTime.ToString("HH:mm:ss.ff");
                        string eTime = entry.endTime.ToString("HH:mm:ss.ff");
                        string content = entry.content.Replace("\n", "<br/>");
                        writer.WriteStartElement("p");
                        writer.WriteAttributeString("begin", sTime);
                        writer.WriteAttributeString("end", eTime);
                        writer.WriteAttributeString("xml", "id", null, "caption " + i);

                        writer.WriteRaw(content);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();//div
                    writer.WriteEndElement();//Body
                    writer.WriteEndElement();//tt
                    writer.Flush();
                }
            }
        }
        /// <summary>
        /// Converts the local format to Subrip format
        /// </summary>
        /// <param name="path">Output path for subtitle</param>
        private void WriteSRT(string path)
        {
            string nlSRT = "\n";
            if (subtitleNewLineOption != SubtitleNewLineOption.Default)
            {
                nlSRT = nlDict[subtitleNewLineOption];
            }
            StringBuilder subExport = new StringBuilder();
            int i = 0;
            foreach (SubtitleEntry entry in subTitleLocal)
            {
                i++;
                string sTime = entry.startTime.ToString("HH:mm:ss,fff");
                string eTime = entry.endTime.ToString("HH:mm:ss,fff");
                subExport.Append(i+ nlSRT + sTime + " --> " + eTime + nlSRT + entry.content + nlSRT + nlSRT);
            }
            File.WriteAllText(path, subExport.ToString());
        }
        /// <summary>
        /// Writes the Subtite to Subviewer format
        /// </summary>
        /// <param name="path">Output path for subtitle</param>
        private void WriteSubviewer(string path)
        {
            string nlSubv = "\r\n";
            if (subtitleNewLineOption != SubtitleNewLineOption.Default)
            {
                nlSubv = nlDict[subtitleNewLineOption];
            }

            string subHead =   "[INFORMATION]" + nlSubv+
                               "[TITLE]" + nlSubv+
                               "[AUTHOR]" + nlSubv+
                               "[SOURCE]" + nlSubv +
                               "[PRG]" + nlSubv +
                               "[FILEPATH]" + nlSubv +
                               "[DELAY]" + nlSubv +
                               "[CD TRACK]" + nlSubv +
                               "[COMMENT]" + nlSubv +
                               "[END INFORMATION]" + nlSubv +
                               "[SUBTITLE]" + nlSubv +
                               "[COLF]&HFFFFFF,[STYLE]no,[SIZE]18,[FONT]Arial"+ nlSubv;
            StringBuilder subExport = new StringBuilder(subHead);
            foreach (SubtitleEntry entry in subTitleLocal)
            {
                string sTime = entry.startTime.ToString("HH:mm:ss.ff");
                string eTime = entry.endTime.ToString("HH:mm:ss.ff");
                subExport.Append(sTime + "," + eTime + nlSubv + entry.content.Replace("\n", nlSubv) + nlSubv + nlSubv);
            }
            File.WriteAllText(path, subExport.ToString());
        }
        /// <summary>
        /// Converts the local format to Subrip format
        /// Similar to WriteSRT with an additional value added to the start.
        /// </summary>
        /// <param name="path">Output path for subtitle</param>
        private void WriteWebVTT(string path)
        {
            string nlWV = "\n";
            if (subtitleNewLineOption != SubtitleNewLineOption.Default)
            {
                nlWV = nlDict[subtitleNewLineOption];
            }
            StringBuilder subExport =  new StringBuilder("WEBVTT" + nlWV + nlWV);
            int i = 0;
            foreach (SubtitleEntry entry in subTitleLocal)
            {
                i++;
                string sTime = entry.startTime.ToString("HH:mm:ss.fff");
                string eTime = entry.endTime.ToString("HH:mm:ss.fff");
                subExport.Append(i + nlWV + sTime + " --> " + eTime + nlWV + entry.content.Replace("\n\n", nlWV) + nlWV + nlWV); //Double newline only allowed at end;
            }
            File.WriteAllText(path, subExport.ToString());
        }
        /// <summary>
        /// Converts the local format to WebSubrip format
        /// Essentilly the same except as WriteSRT using a different time format
        /// </summary>
        /// <param name="path">Output path for subtitle</param>
        private void WriteWSRT(string path)
        {
            string nlSRT = "\n";
            if (subtitleNewLineOption != SubtitleNewLineOption.Default)
            {
                nlSRT = nlDict[subtitleNewLineOption];
            }
            StringBuilder subExport = new StringBuilder();
            int i = 0;
            foreach (SubtitleEntry entry in subTitleLocal)
            {
                i++;
                string sTime = entry.startTime.ToString("HH:mm:ss.fff");
                string eTime = entry.endTime.ToString("HH:mm:ss.fff");
                subExport.Append(i + nlSRT + sTime + " --> " + eTime + nlSRT + entry.content + nlSRT + nlSRT);
            }
            File.WriteAllText(path, subExport.ToString());
        }

        //--------------------------------------------Misc stuff -----------------//
        /// <summary>
        ///Remove dupicale start times and join to one
        ///Assumes the subs are sorted 
        ///Taken from and modified from http://stackoverflow.com/questions/14918668/find-duplicates-and-merge-items-in-a-list  
        /// </summary>
        private void JoinSameStart()
        {
            for (int i = 0; i < subTitleLocal.Count - 1; i++)
            {
                var item = subTitleLocal[i];
                for (int j = i + 1; j < subTitleLocal.Count; )
                {
                    var anotherItem = subTitleLocal[j];
                    if (item.startTime > anotherItem.startTime) break; //No point contiuning as the list is sorted
                    if (item.startTime.Equals(anotherItem.startTime))
                    {
                        //We just join the to and hope that they were in the right order
                        //TODO: check y offset and order by that
                        item.content = item.content + "\n" + anotherItem.content;
                        subTitleLocal.RemoveAt(j);
                    }
                    else
                        j++;
                }
            }
        }

        /// <summary>
        /// Parses a timemetric ie 12h31m2s44ms
        /// </summary>
        /// <param name="metric">The metric string</param>
        /// <returns>The datetime equivilent</returns>
        private DateTime ParseTimeMetric(string metric)
        {
            DateTime time = new DateTime();
            Regex rg = new Regex(@"([0-9.]+)([a-z]+)");
            MatchCollection mtchs = rg.Matches(metric);
            foreach(Match match in mtchs)
            {
                float st = float.Parse(match.Groups[1].Value);
                switch (match.Groups[2].Value)
                {
                    case ("h"):
                        time = time.AddHours(st);
                        break;
                    case ("m"):
                        time = time.AddMinutes(st);
                        break;
                    case ("s"):
                        time = time.AddSeconds(st);
                        break;
                    case ("ms"):
                        time = time.AddMilliseconds(st);
                        break;
                }
            }
            return time;
        }
        /// <summary>
        /// Parses a timemetric ie 12h31m2s44ms
        /// </summary>
        /// <param name="metric">The metric string</param>
        /// <returns>The TimeSpan equivilent</returns>
        private TimeSpan ParseTimeMetricAsTimeSpan(string metric)
        {
            TimeSpan time = new TimeSpan();
            Regex rg = new Regex(@"([0-9.]+)([a-z]+)");
            MatchCollection mtchs = rg.Matches(metric);
            foreach (Match match in mtchs)
            {
                float st = float.Parse(match.Groups[1].Value);
                switch (match.Groups[2].Value)
                {
                    case ("h"):
                        time += TimeSpan.FromHours(st);
                        break;
                    case ("m"):
                        time += TimeSpan.FromMinutes(st);
                        break;
                    case ("s"):
                        time += TimeSpan.FromSeconds(st);
                        break;
                    case ("ms"):
                        time += TimeSpan.FromMilliseconds(st);
                        break;
                }
            }
            return time;
        }

        /// <summary>
        /// Parses a timemetric ie 12h31m2s44ms
        /// </summary>
        /// <param name="metric">The metric string</param>
        /// <returns>The Timespan equivilent</returns>
        private TimeSpan ParseTimeMetricTimeSpan(string metric)
        {
            TimeSpan time = TimeSpan.Zero;
            Regex rg = new Regex(@"([0-9.]+)([a-z]+)");
            MatchCollection mtchs = rg.Matches(metric);
            foreach (Match match in mtchs)
            {
                float st = float.Parse(match.Groups[1].Value);
                switch (match.Groups[2].Value)
                {
                    case ("h"):
                        time = time.Add(TimeSpan.FromHours(st));
                        break;
                    case ("m"):
                        time = time.Add(TimeSpan.FromMinutes(st));
                        break;
                    case ("s"):
                        time = time.Add(TimeSpan.FromSeconds(st));
                        break;
                    case ("ms"):
                        time = time.Add(TimeSpan.FromMilliseconds(st));
                        break;
                }
            }
            return time;
        }

        /// <summary>
        /// Converts an int representing frames to a datetime object
        /// </summary>
        /// <param name="frames">The number of frames</param>
        /// <param name="fps">The frames per second</param>
        /// <returns>The created datetime</returns>
        private DateTime framesToDateTime(int frames, float fps)
        {
            DateTime dt = new DateTime();
            dt = dt.AddSeconds(frames / fps);
            return dt;
        }

        /// <summary>
        /// Adds the supplied timemetic to all entries in the subtitle list
        /// </summary>
        /// <param name="timeMetric">A time metic to adjust the subtitle</param>
        public void AdjustTimingLocalAdd(string timeMetric)
        {
            TimeSpan ts = ParseTimeMetricTimeSpan(timeMetric);
            foreach(SubtitleEntry entry in subTitleLocal)
            {
                entry.startTime = entry.startTime.Add(ts);
                entry.endTime = entry.endTime.Add(ts);
            }
        }

        /// <summary>
        /// Subtract the supplied timemetic to all entries in the subtitle list
        /// </summary>
        /// <param name="timeMetric">A time metic to adjust the subtitle</param>
        public void AdjustTimingLocalSub(string timeMetric)
        {
            TimeSpan ts = ParseTimeMetricTimeSpan(timeMetric);
            foreach (SubtitleEntry entry in subTitleLocal)
            {
                DateTime sTNew  = entry.startTime.Subtract(ts);
                DateTime eTNew = entry.endTime.Subtract(ts);
                if(sTNew.DayOfYear == entry.startTime.DayOfYear) //Need to check if it underflowed
                {
                    entry.startTime = sTNew;
                }
                else                                             //It underflowed
                {
                    entry.startTime = new DateTime(entry.startTime.Year, entry.startTime.Month, entry.startTime.Day, 0, 0, 0, 0, entry.startTime.Kind);
                }

                if(eTNew.DayOfYear == entry.endTime.DayOfYear) //Need to check if it underflowed
                {
                    entry.endTime = eTNew;
                }
                else                                             //It underflowed
                {
                    entry.endTime = new DateTime(entry.endTime.Year, entry.endTime.Month, entry.endTime.Day, 0, 0, 0,0, entry.endTime.Kind);
                }

            }
        }

        /// <summary>
        /// Read a subtitle from the specified input path / extension
        /// </summary>
        /// <param name="input">Path to the subtitle</param>
        /// <returns>A boolean representing the success of the operation</returns>
        public bool ReadSubtitle(string input)
        {
            subTitleLocal = new List<SubtitleEntry>();
            string extensionInput = Path.GetExtension(input).ToLower();
            switch (extensionInput) //Read file
            {
                case (".ass"):
                case (".ssa"):
                    ReadASS(input);
                    break;
                case (".dfxp"):
                case (".ttml"):
                    ReadDFXP(input);
                    break;
                case (".sub"):
                    ReadSub(input);
                    break;
                case (".srt"):
                    ReadSRT(input);
                    break;
                case (".vtt"):
                    ReadWebVTT(input);
                    break;
                case (".wsrt"):
                    ReadWSRT2(input);
                    break;
                default:
                    Console.WriteLine("Invalid read file format");
                    return false;
            }
            return true;
        }
        /// <summary>
        /// Writes a subtitle to the specified output path / extension
        /// </summary>
        /// <param name="output">Output path location with file extension</param>
        /// <returns>A boolean representing the success of the operation</returns>
        public bool WriteSubtitle(string output)
        {
            string extensionOutput = Path.GetExtension(output).ToLower();

            switch (extensionOutput) //Write to file
            {
                case (".ass"):
                case (".ssa"):
                    WriteASS(output);
                    break;
                case (".dfxp"):
                case (".ttml"):
                    WriteDFXP(output);
                    break;
                case (".sub"):
                    WriteSubviewer(output);
                    break;
                case (".srt"):
                    WriteSRT(output);
                    break;
                case (".vtt"):
                    WriteWebVTT(output);
                    break;
                case (".wsrt"):
                    WriteWSRT(output);
                    break;
                default:
                    Console.WriteLine("Invalid write file format");
                    return false;
            }
            return true;
        }
        /// <summary>
        /// Convert a subtitle, supports specififed by the input and output file extension
        /// ASS/SSA DFXP/TTML, SUB, SRT, WSRT, VTT;
        /// </summary>
        /// <param name="input">The path to the subtitle to convert</param>
        /// <param name="output">The path to the location to save, and file name/type to convert to</param>
        /// <returns>A boolean representing the success of the operation</returns>
        public bool ConvertSubtitle(string input, string output)
        {
            return ConvertSubtitle(input, output, "");
        }
        /// <summary>
        /// Convert a subtitle, supports specififed by the input and output file extension
        /// ASS/SSA DFXP/TTML, SUB, SRT, WSRT, VTT;
        /// </summary>
        /// <param name="input">The path to the subtitle to convert</param>
        /// <param name="output">The path to the location to save, and file name/type to convert to</param>
        /// <param name="timeshift"> The time to shift the subtitle</param>
        /// <returns>A boolean representing the success of the operation</returns>
        public bool ConvertSubtitle(string input, string output, string timeshift)
        {

            if (!ReadSubtitle(input)) return false;
            if (!timeshift.Equals(""))//Adjust time
            {
                if (timeshift[0] == '-') AdjustTimingLocalSub(timeshift);
                else AdjustTimingLocalAdd(timeshift);
            }
            if (!WriteSubtitle(output)) return false;
            return true;
        }
    }
}
