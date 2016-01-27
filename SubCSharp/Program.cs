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
        static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: SubCSharp.exe input output");
                return 1;
            }
            if(File.Exists(args[0]) && Uri.IsWellFormedUriString(args[1],UriKind.RelativeOrAbsolute))// file in/out
            {
                SubtitleConverter subConv = new SubtitleConverter();
                subConv.ConvertSubtitle(args[0], args[1]);
                Console.WriteLine("Complete");
                return 0;                
            }
            Console.WriteLine("Invalid path for input/output");
            return 2;
        }
    }
}
