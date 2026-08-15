using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks_Lib;

namespace ExtractBOM
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Starting ExtractBOM Script...");
            Console.WriteLine("----------------------------------");

            // 1. Retrieve File Path
            string? filePath;
            if (args.Length > 0)
            {
                filePath = args[0];
            }
            else
            {
                Console.Write("Please enter the full path of the SolidWorks assembly file to extract BOM from: ");
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

                // 4. Extract Bill of Materials (BOM)
                Console.WriteLine("Calculating Bill of Materials (BOM)...");
                Dictionary<string, int> bomData = swEngine.Generate_BOM();

                if (bomData == null || bomData.Count == 0)
                {
                    Console.WriteLine("Could not extract BOM data from assembly or assembly is empty.");
                }
                else
                {
                    Console.WriteLine($"Found {bomData.Count} distinct component type(s). Exporting to CSV...");
                    
                    // 5. Export Output to CSV
                    string baseFolder = Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    string csvPath = Path.Combine(baseFolder, "BOM_Report.csv");
                    swEngine.Export_Dict_To_Csv(bomData, csvPath);
                    
                    Console.WriteLine($"Success! Bill of Materials report saved to: {csvPath}");
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
