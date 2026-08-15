using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks_Lib;

namespace InspectFillet
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Starting InspectFillet Script...");
            Console.WriteLine("----------------------------------");

            // 1. Retrieve File Path
            string? filePath;
            if (args.Length > 0)
            {
                filePath = args[0];
            }
            else
            {
                Console.Write("Please enter the full path of the SolidWorks file to analyze: ");
                filePath = Console.ReadLine();
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Console.WriteLine("Error: Valid file path was not provided or file not found.");
                return;
            }

            // 2. Initialize SolidWorksEngine
            // IMPORTANT: SolidWorks must run in GUI mode (head: true) to capture screenshots!
            SolidWorksEngine swEngine = new SolidWorksEngine();
            Console.WriteLine("Starting SolidWorks (in GUI mode for screenshot capture)...");

            bool isStarted = swEngine.Start_SW(true);
            if (!isStarted)
            {
                Console.WriteLine("Error: Failed to start SolidWorks.");
                return;
            }

            try
            {
                // 3. Open Document
                Console.WriteLine($"Opening file: {filePath}");
                bool isOpened = swEngine.Open_Document(filePath);

                if (!isOpened)
                {
                    Console.WriteLine("Error: Could not open document.");
                    return;
                }

                // 4. Analyze Fillet Features
                Console.WriteLine("Scanning fillet / radius features...");
                Dictionary<string, double> fillets = swEngine.Inspect_Fillets();

                if (fillets == null || fillets.Count == 0)
                {
                    Console.WriteLine("No fillet features found in the model.");
                }
                else
                {
                    Console.WriteLine($"Found {fillets.Count} fillet feature(s) in total.");

                    // 5. Configure Output Folders
                    string baseFolder = Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    string photosFolder = Path.Combine(baseFolder, "screenshots");
                    string csvPath = Path.Combine(baseFolder, "Fillet_Analysis_Report.csv");

                    if (!Directory.Exists(photosFolder))
                    {
                        Directory.CreateDirectory(photosFolder);
                    }

                    // 6. Capture Screenshots
                    Console.WriteLine("Capturing screenshots, please do not interact with the SolidWorks window...");
                    foreach (var fillet in fillets)
                    {
                        string featName = fillet.Key;
                        Console.WriteLine($"- Capturing {featName}...");
                        bool success = swEngine.Take_Feature_Screenshot(featName, photosFolder);
                        if (!success)
                        {
                            Console.WriteLine($"  Warning: Could not capture screenshot for {featName}.");
                        }
                    }

                    // 7. Export to CSV
                    Console.WriteLine("Exporting data to CSV...");
                    // Using built-in export utility from SolidWorks_Lib
                    swEngine.Export_Dict_To_Csv(fillets, csvPath);

                    Console.WriteLine($"\nSuccess!\n- Screenshots: {photosFolder}\n- CSV Report: {csvPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
            finally
            {
                // 8. Close SolidWorks
                Console.WriteLine("Closing SolidWorks session...");
                swEngine.Stop_SW();
                Console.WriteLine("Process complete. Press any key to exit...");
                Console.ReadKey();
            }
        }
    }
}
