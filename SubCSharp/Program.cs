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
            if (args.Length < 2 || args.Length > 3)
            {
                Console.WriteLine("Usage: SubCSharp.exe input output timeshift(optional)");
                return 1;
            }
            if(File.Exists(args[0]) && Uri.IsWellFormedUriString(args[1],UriKind.RelativeOrAbsolute))// file in/out
            {
                SubtitleConverter subConv = new SubtitleConverter();
                if (args.Length == 3)
                {
                    subConv.ConvertSubtitle(args[0], args[1], args[2]);
                }
                else if(subConv.ConvertSubtitle(args[0], args[1]))
                {
                    Console.WriteLine("Complete");
                    return 0;
                }
               return 3;                
            }
            Console.WriteLine("Invalid path for input/output");
            return 2;
        }
    }
}
