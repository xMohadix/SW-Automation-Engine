using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks_Lib;

namespace InterferenceAnalysis
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Starting InterferenceAnalysis Script...");
            Console.WriteLine("----------------------------------------");

            // 1. Retrieve File Path
            string? filePath;
            if (args.Length > 0)
            {
                filePath = args[0];
            }
            else
            {
                Console.Write("Please enter the full path of the SolidWorks assembly file to analyze for interferences: ");
                filePath = Console.ReadLine();
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Console.WriteLine("Error: Valid file path was not provided or file not found.");
                return;
            }

            // 2. Initialize SolidWorksEngine (Headless mode = false)
            SolidWorksEngine swEngine = new SolidWorksEngine();
            Console.WriteLine("Starting SolidWorks in background (headless mode)...");

            bool isStarted = swEngine.Start_SW(false);
            if (!isStarted)
            {
                Console.WriteLine("Error: Failed to start SolidWorks.");
                return;
            }

            try
            {
                // 3. Open Document
                Console.WriteLine($"Opening assembly: {filePath}");
                bool isOpened = swEngine.Open_Document(filePath);

                if (!isOpened)
                {
                    Console.WriteLine("Error: Could not open document.");
                    return;
                }

                // Prompt user for fastener/screw inclusion
                Console.Write("Include screws/fasteners in interference analysis? (Y/N) [Default: Y]: ");
                string? screwInput = Console.ReadLine()?.Trim().ToUpper();
                bool includeScrews = (screwInput != "N" && screwInput != "H");

                // Prompt user for coincident face handling
                Console.Write("Treat coincident (touching) mating faces as interferences? (Y/N) [Default: N]: ");
                string? coincidentInput = Console.ReadLine()?.Trim().ToUpper();
                bool treatCoincidence = (coincidentInput == "Y" || coincidentInput == "E");

                // 4. Run Interference Detection
                Console.WriteLine("Calculating physical interferences, please wait...");

                // Get_Interferences returns Dictionary<string, string>
                Dictionary<string, string> interferences = swEngine.Get_Interferences(treatCoincidence, includeScrews);

                if (interferences == null || interferences.Count == 0)
                {
                    Console.WriteLine("Success! No physical interferences detected in the assembly.");
                }
                else
                {
                    // Filter duplicate reciprocal collision pairs (A-B and B-A):
                    Console.WriteLine("Filtering redundant reciprocal collision pairs...");
                    Dictionary<string, string> uniqueInterferences = swEngine.Eliminate_Duplicate_Collisions(interferences);

                    Console.WriteLine($"Unique collision pairs detected: {uniqueInterferences.Count}");

                    // 5. Export Output to CSV
                    string baseFolder = Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    string csvPath = Path.Combine(baseFolder, "Interference_Report.csv");
                    swEngine.Export_Dict_To_Csv(uniqueInterferences, csvPath);

                    Console.WriteLine($"Success! Interference report saved to: {csvPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
            finally
            {
                // 6. Close SolidWorks
                Console.WriteLine("Closing SolidWorks session...");
                swEngine.Stop_SW();
                Console.WriteLine("Process complete. Press any key to exit...");
                Console.ReadKey();
            }
        }
    }
}
