using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SubCSharp
{
    class Program
    {
        enum ExitCodes {Success, InvalidArguments, IOError}
        //Temp solution
        static int Main(string[] args)
        {
            String inputPath = "";
            String outputPath = "";
            String timeShift = "";

            if(args.Length % 2 != 0)
            {
                Console.WriteLine("Usage: SubCSharp.exe -i input -o output [-t timeshift]");
                return (int)ExitCodes.InvalidArguments;
            }

            //Read line arguments
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("-i"))
                {
                    inputPath = args[++i];
                }
                else if (args[i].Equals("-o"))
                {
                    outputPath = args[++i];
                }
                else if (args[i].Equals("-t"))
                {
                    timeShift = args[++i];
                }
                else
                {
                    Console.WriteLine("Unknown command {0} ", args[i]);
                    Console.WriteLine("Usage: SubCSharp.exe -i input -o output [-t timeshift]");
                    return (int)ExitCodes.InvalidArguments;
                }
            }
            if(inputPath.Equals("") || outputPath.Equals(""))
            {
                Console.WriteLine("Usage: SubCSharp.exe -i input -o output [-t timeshift]");
                return (int)ExitCodes.IOError;
            }

            if (File.Exists(inputPath) && Uri.IsWellFormedUriString(outputPath, UriKind.RelativeOrAbsolute))// file in/out
            {
                SubtitleConverter subConv = new SubtitleConverter();
                subConv.ConvertSubtitle(inputPath, outputPath, timeShift);
                Console.WriteLine("Complete");
                return (int)ExitCodes.Success;
            }
            else
            {
                Console.WriteLine("Invalid path for input/output");
                return (int)ExitCodes.IOError;
            }
        }
    }
}
