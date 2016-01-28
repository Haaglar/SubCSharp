using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace SubCSharp
{
    public class SubtitleConverter
    {
        //State for WSRT & Webvvt reading
        private enum SState { Empty, Adding, Iterating, Comment, Timestamp };
        private static String[] SpaceArray = new String[] { " " }; //Dont want to keep recreating it
        private static String[] NewLineArray = new String[] { "\n" };
        private static String[] CommaArray = new String[] { "," };

        //Internal sub format to allow easy conversion
        private class SubtitleEntry
        {
            public DateTime startTime { get; set; }
            public DateTime endTime { get; set; }
            public String content { get; set; }
            public SubtitleEntry(DateTime sTime, DateTime eTime, String text)
            {
                startTime = sTime;
                endTime = eTime;
                content = text;
            }
        }

        List<SubtitleEntry> subTitleLocal;

        public SubtitleConverter() { }
        //-------------------------------------------------------------------------Read Formats---------------//

        private void ReadASS(String path)
        {
            String subContent;// = File.ReadAllText(path, Encoding.Default);
            using (StreamReader assFileSR = new StreamReader(path)) //Read file to string
            {
                subContent = assFileSR.ReadToEnd();
                subContent = Regex.Replace(subContent, @"\{[^}]*\}", ""); //Remove all additional styling
            }
            using (StringReader assFile = new StringReader(subContent)) 
            {
                String line = "";
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
                                String [] splitLine = line.Split(CommaArray, 10 , StringSplitOptions.None);
                                DateTime beginTime = DateTime.ParseExact(splitLine[1], "H:mm:ss.ff", CultureInfo.InvariantCulture);
                                DateTime endTime = DateTime.ParseExact(splitLine[2], "H:mm:ss.ff", CultureInfo.InvariantCulture);
                                String text = splitLine[9].Replace("\\N", "\n");//Replace new lineswith actual newlines
                                subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, text));
                            }
                            break;
                    }
                }
            }
            //Since ass/ssa can be in any order we must do this;
            //Taken from and modified from http://stackoverflow.com/questions/14918668/find-duplicates-and-merge-items-in-a-list
            for (int i = 0; i < subTitleLocal.Count - 1; i++)
            {
                var item = subTitleLocal[i];
                for (int j = i + 1; j < subTitleLocal.Count; )
                {
                    var anotherItem = subTitleLocal[j];
                    if (item.startTime.Equals(anotherItem.startTime)) // i.e., dictionaries are equal
                    {
                        item.content = item.content +"\n" + anotherItem.content;
                        subTitleLocal.RemoveAt(j); // it has better performance compared to `StaticList.Remove(anotherItem);`
                    }
                    else
                        j++;
                }
            }
        }
        /// <summary>
        /// Converts a dfxp subtitle into the Catchup Grabbers subtitle format
        /// </summary>
        /// <param name="path">The path to the dfxp to convert</param>
        private void ReadDFXP(String path)
        {
            String raw = File.ReadAllText(path);
            System.IO.File.WriteAllText(path + "_cureadtemp", raw.Replace("\r\n", "\n")); //Need to work with a unix format

            using (XmlTextReader reader = new XmlTextReader(path + "_cureadtemp"))
            {
                reader.Namespaces = false;// Namespaces are annoying, screw them.
                while (reader.ReadToFollowing("p")) //Read all p nodes
                {
                    DateTime beginTime;
                    DateTime endTime;
                    String begin = reader.GetAttribute("begin");
                    bool beginSuc = DateTime.TryParse(begin, out beginTime);
                    if (!beginSuc) //If that failed parse it differently
                    {
                        beginTime = parseTimeMetric(begin);
                    }

                    String end = reader.GetAttribute("end");
                    bool endSuc = DateTime.TryParse(end, out endTime);

                    if (!endSuc) //If that failed parse it differently
                    {
                        endTime = parseTimeMetric(end);
                    }

                    String text = reader.ReadInnerXml();
                    text = Regex.Replace(text, "\n( *)", ""); //Debeutify xml node
                    text = text.Replace("<br /><br />", "\n").Replace("<br/><br/>", "\n").Replace("<br />", "\n").Replace("<br/>", "\n"); //Depends on the format remove all
                    subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, text));
                }
            }
            System.IO.File.Delete(path + "_cureadtemp"); //Remove temp read file
        }

        /// <summary>
        /// Converts a srt subtitle into the Catchup Grabbers subtitle format
        /// </summary>
        /// <param name="path">Input path for the subtitle</param>
        private void ReadSRT(String path)
        {
            String raw = File.ReadAllText(path);
            String[] split = Regex.Split(raw, @"\n\n[0-9]+\n"); //Each etnry can be separted like this, a subtitle cannot contain a blank line followed by a line containing only a decimal number appartently
            //First case is a bit different as it has an extra row or maybe junk
            String case1 = split[0].TrimStart();
            String[] splitc1 = case1.Split(new string[] { "\n" }, StringSplitOptions.None);

            String[] time = Regex.Split(splitc1[1], " *--> *");           //May or may not have a space or more than 2 dashes

            DateTime beginTime;
            DateTime endTime;

            beginTime = DateTime.ParseExact(time[0], "HH:mm:ss,fff", CultureInfo.InvariantCulture);
            endTime = DateTime.ParseExact(time[1], "HH:mm:ss,fff", CultureInfo.InvariantCulture);

            String tmp = splitc1[2];
            foreach (String text in splitc1.Skip(3))
            {
                tmp = tmp + "\n" + text;
            }

            subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, tmp));
            //Main loop
            foreach (String sub in split.Skip(2))
            {
                String[] splitc2 = sub.Split(new string[] { "\n" }, StringSplitOptions.None);

                String[] time2 = Regex.Split(splitc2[0], " *--> *");           //May or may not have a space or more than 2 dashes
                DateTime beginTime2;
                DateTime endTime2;
                beginTime2 = DateTime.ParseExact(time2[0], "HH:mm:ss,fff", CultureInfo.InvariantCulture);
                endTime2 = DateTime.ParseExact(time2[1], "HH:mm:ss,fff", CultureInfo.InvariantCulture);

                String tmp2 = splitc2[1].TrimEnd();
                foreach (String text in splitc2.Skip(2))
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
        private void ReadWebVTT(String path)
        {
            String raw = File.ReadAllText(path);
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
            String textContent = "";
            foreach (String line in splited.Skip(1)) //Iterate line by line
            {
                switch (ss)
                {
                    case (SState.Empty):
                        String linetrim = line.TrimEnd();
                        if (line.Equals("")) continue;                            //Run past newlines
                        //Style is only allowed to appear before all cues, hence a separate test, 
                        //unsure if you can have style and a :: value on same line, so test both  anyway
                        if (subTitleLocal.Count == 0 && linetrim.Equals("STYLE") || linetrim.StartsWith("STYLE "))
                        {
                            ss = SState.Comment;                                //We want to skip like a note;
                            goto case (SState.Comment);                         //Goto encouraged in in c# case :)
                        }
                        //WEBVTTComment, or region values we'll just skip
                        if (line.Equals("NOTE") || linetrim.StartsWith("NOTE ") || linetrim.Equals("REGION"))
                        {
                            ss = SState.Comment;
                            goto case (SState.Comment);
                        }
                        if (line.Contains("-->")) goto case (SState.Timestamp); //As we dont care for Queue ID, test only for timestamp
                        break;

                    case (SState.Timestamp):
                        //Split and parse the timestamp 
                        String[] time = Regex.Split(line, " *--> *");
                        //Parse the time, can only be one of 2 option, so try this one first
                        bool tryBegin = DateTime.TryParseExact(time[0], "HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out beginTime);
                        String[] endTimeSplit = time[1].Split(SpaceArray, StringSplitOptions.None); //Timestamp might contain info after it, so remove it
                        bool tryEnd = DateTime.TryParseExact(endTimeSplit[0], "HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out endTime);
                        //If something went wrong, parse it differnetly;
                        if (!tryBegin) DateTime.TryParseExact(time[0], "mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out beginTime);
                        if (!tryEnd) DateTime.TryParseExact(endTimeSplit[0], "mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out endTime);
                        ss = SState.Iterating;
                        break;

                    case (SState.Iterating):
                        if (line.Equals("")) //Come to the end of the cue block so add it to the sub
                        {
                            String cleanedString = textContent.TrimEnd(); //Remove the additional newline we added
                            subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, cleanedString));
                            textContent = "";                             //Cleanup
                            ss = SState.Empty;
                        }
                        else textContent += line + "\n"; //Otherwise just add to the cue content;
                        break;

                    case (SState.Comment): //We dont want notes so lets go here
                        if (line.Equals("")) ss = SState.Empty;//Reached the end of the comment/style/region;
                        break;
                }
            }
            if (ss == SState.Iterating) //End of file, add last if we were still going
            {
                String cleanedString = textContent.TrimEnd(); //Remove the additional newline we added
                subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, cleanedString));
            }

        }
        /// <summary>
        /// Converts a wsrt subtitle into the Catchup Grabbers subtitle format
        /// Old, Use ReadWSRT2 instead
        /// </summary>
        /// <param name="path">Input path for the subtitle</param>
        private void ReadWSRT(String path)
        {
            //Very similar to ReadSRT, however removes additions such as cues settings
            // Neeed to support addition info at http://annodex.net/~silvia/tmp/WebSRT/#slide1
            String raw = File.ReadAllText(path);
            raw = raw.Replace("\r\n", "\n");
            String[] split = Regex.Split(raw, @"\n\n[0-9]+\n"); //Each etnry can be separted like this
            //First case is a bit different as it has an extra row or maybe junk
            String case1 = split[0].TrimStart();
            String[] splitc1 = case1.Split(NewLineArray, StringSplitOptions.None);

            String[] time = Regex.Split(splitc1[1], " *--> *");           //May or may not have a space or more than 2 dashes

            DateTime beginTime;
            DateTime endTime;

            beginTime = DateTime.ParseExact(time[0], "HH:mm:ss.fff", CultureInfo.InvariantCulture);
            endTime = DateTime.ParseExact(time[1], "HH:mm:ss.fff", CultureInfo.InvariantCulture);

            String tmp = splitc1[2];
            foreach (String text in splitc1.Skip(3))
            {
                tmp = tmp + "\n" + text;
            }
            tmp = Regex.Replace(tmp, @"</*[0-9]+>", "");
            subTitleLocal.Add(new SubtitleEntry(beginTime, endTime, tmp));
            //Main loop
            foreach (String sub in split.Skip(1))
            {
                String[] splitc2 = sub.Split(NewLineArray, StringSplitOptions.None);

                String[] time2 = Regex.Split(splitc2[0], " *--> *");           //May or may not have a space or more than 2 dashes
                DateTime beginTime2;
                DateTime endTime2;
                beginTime2 = DateTime.ParseExact(time[0].Substring(0, 12), "HH:mm:ss.fff", CultureInfo.InvariantCulture);
                endTime2 = DateTime.ParseExact(time[1].Substring(0, 12), "HH:mm:ss.fff", CultureInfo.InvariantCulture);

                String tmp2 = splitc2[1].TrimEnd();
                foreach (String text in splitc2.Skip(2))
                {
                    tmp2 = tmp2 + "\n" + text.TrimEnd();
                }
                tmp2 = Regex.Replace(tmp2, @"</*[0-9]+>", "");
                subTitleLocal.Add(new SubtitleEntry(beginTime2, endTime2, tmp2));
            }

        }
        /// <summary>
        /// Converts a wsrt subtitle into the Catchup Grabbers subtitle format
        /// </summary>
        /// <param name="path">Path to the WSRT file</param>
        private void ReadWSRT2(String path)
        {

            String raw = File.ReadAllText(path);
            raw = raw.Replace("\r\n", "\n");
            raw = raw.Trim();
            var splited = raw.Split(NewLineArray, StringSplitOptions.None).ToList();
            DateTime beginTime = new DateTime();
            DateTime endTime = new DateTime();
            String previous = "<blankstring>"; //Since need to handle first time option and can
            String subContent = "";
            SState ss = SState.Empty;
            String cleanedString = "";
            foreach (String line in splited)
            {
                switch (ss)
                {
                    case (SState.Empty): //First time
                        if (line.Contains("-->"))
                        {
                            String[] time = Regex.Split(line, " *--> *");
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
                            String[] time = Regex.Split(line, " *--> *");
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
        /// Writes the current subtitle stored as a DFXP
        /// </summary>
        /// <param name="path">Output path for subtitle</param>
        private void WriteDFXP(String path)
        {
            String output;
            using (var ms = new MemoryStream())
            {
                using (XmlTextWriter writer = new XmlTextWriter(ms, System.Text.Encoding.UTF8))
                {
                    writer.Formatting = Formatting.Indented;
                    writer.Indentation = 2;

                    writer.WriteStartDocument();
                    writer.WriteStartElement("tt", "http://www.w3.org/ns/ttml");
                    writer.WriteStartElement("body");
                    writer.WriteStartElement("div");
                    writer.WriteAttributeString("xml", "lang", null, "en");
                    int i = 0;
                    foreach (SubtitleEntry entry in subTitleLocal)
                    {
                        i++;
                        String sTime = entry.startTime.ToString("HH:mm:ss.ff");
                        String eTime = entry.endTime.ToString("HH:mm:ss.ff");
                        String content = entry.content.Replace("\n", "<br/>");
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
                output = Encoding.UTF8.GetString(ms.ToArray());
            }
            System.IO.File.WriteAllText(path, output.Replace("\r\n", "\n")); //Cause XmlTextWrite writes windows formatting          
        }
        /// <summary>
        /// Converts the local format to Subrip format
        /// </summary>
        /// <param name="path">The path to the save location</param>
        private void WriteSRT(String path)
        {
            String subExport = "";
            int i = 0;
            foreach (SubtitleEntry entry in subTitleLocal)
            {
                i++;
                String sTime = entry.startTime.ToString("HH:mm:ss,fff");
                String eTime = entry.endTime.ToString("HH:mm:ss,fff");
                subExport = subExport + i + "\n" + sTime + " --> " + eTime + "\n" + entry.content + "\n" + "\n";
            }
            System.IO.File.WriteAllText(path, subExport);
        }
        /// <summary>
        /// Converts the local format to Subrip format
        /// Similar to WriteSRT with an additional value added to the start;
        /// </summary>
        /// <param name="path"></param>
        private void WriteWebVTT(String path)
        {
            String subExport = "WEBVTT\n\n";
            int i = 0;
            foreach (SubtitleEntry entry in subTitleLocal)
            {
                i++;
                String sTime = entry.startTime.ToString("HH:mm:ss.fff");
                String eTime = entry.endTime.ToString("HH:mm:ss.fff");
                subExport = subExport + i + "\n" + sTime + " --> " + eTime + "\n" + entry.content.Replace("\n\n", "\n") + "\n" + "\n"; //Double newline only allowed at end;
            }
            System.IO.File.WriteAllText(path, subExport);
        }
        /// <summary>
        /// Converts the local format to WebSubrip format
        /// Essentilly the same except as WriteSRT using a different time format
        /// </summary>
        /// <param name="path">The path to the location to save to</param>
        private void WriteWSRT(String path)
        {
            String subExport = "";
            int i = 0;
            foreach (SubtitleEntry entry in subTitleLocal)
            {
                i++;
                String sTime = entry.startTime.ToString("HH:mm:ss.fff");
                String eTime = entry.endTime.ToString("HH:mm:ss.fff");
                subExport = subExport + i + "\n" + sTime + " --> " + eTime + "\n" + entry.content + "\n" + "\n";
            }
            System.IO.File.WriteAllText(path, subExport);
        }

        //--------------------------------------------Misc stuff -----------------//
        /// <summary>
        /// Parses a timemetric ie 12h31m2s44ms
        /// </summary>
        /// <param name="metric">The metric string</param>
        /// <returns>The datetime equivilent</returns>
        private DateTime parseTimeMetric(String metric)
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
        /// Convert a subtitle, supports
        /// DFXP/TTML, SRT, WSRT, vtt;
        /// </summary>
        /// <param name="input">The path to the subtitle to convert</param>
        /// <param name="output">The path to the location to save, and file name/type to convert to</param>
        public bool ConvertSubtitle(String input, String output)
        {
            subTitleLocal = new List<SubtitleEntry>();
            String extensionInput = System.IO.Path.GetExtension(input).ToLower();
            String extensionOutput = System.IO.Path.GetExtension(output).ToLower();
            switch (extensionInput) //Read file
            {
                case (".ass"): case(".ssa"):
                    ReadASS(input);
                    break;
                case (".dfxp"): case(".ttml"):
                    ReadDFXP(input);
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
            switch (extensionOutput) //Write to file
            {
                case (".dfxp"): case(".ttml"):
                    WriteDFXP(output);
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
    }
}
