using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks_Lib;

namespace AnalyzeHoles
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Starting AnalyzeHoles Script...");
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
                Console.WriteLine($"Opening file: {filePath}");
                bool isOpened = swEngine.Open_Document(filePath);

                if (!isOpened)
                {
                    Console.WriteLine("Error: Could not open document.");
                    return;
                }

                // 4. Retrieve Hole Wizard Data
                Console.WriteLine("Scanning Hole Wizard feature data...");
                List<HoleWizardData> holeDataList = swEngine.Inspect_Hole_Wizards();

                if (holeDataList == null || holeDataList.Count == 0)
                {
                    Console.WriteLine("No Hole Wizard features found in the model.");
                }
                else
                {
                    Console.WriteLine($"Found {holeDataList.Count} hole(s) in total. Exporting to CSV...");

                    // 5. Export Output to CSV
                    string baseFolder = Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    string csvPath = Path.Combine(baseFolder, "Hole_Analysis_Report.csv");
                    ExportToCsv(holeDataList, csvPath);

                    Console.WriteLine($"Success! Results saved to: {csvPath}");
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

        // Helper function to export List<HoleWizardData> to a detailed CSV format
        static void ExportToCsv(List<HoleWizardData> dataList, string filePath)
        {
            if (dataList == null || dataList.Count == 0) return;

            List<string> csvLines = new List<string>();

            // Header
            csvLines.Add("FeatureName,HoleType,FastenerSize,Standard,HoleDiameter,NominalDiameter,HoleDepth,ThreadDepth,IsThroughAll,EndCondition,IsFlagged,FlagReason");

            // Data rows
            foreach (var hole in dataList)
            {
                string line = $"{EscapeCsv(hole.FeatureName)},{EscapeCsv(hole.HoleType)},{EscapeCsv(hole.FastenerSize)},{EscapeCsv(hole.Standard)},{hole.HoleDiameter},{hole.NominalDiameter},{hole.HoleDepth},{hole.ThreadDepth},{hole.IsThroughAll},{EscapeCsv(hole.EndCondition)},{hole.IsFlagged},{EscapeCsv(hole.FlagReason)}";
                csvLines.Add(line);
            }

            File.WriteAllLines(filePath, csvLines);
        }

        // Helper function to escape commas for CSV compatibility
        static string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Contains(",")) return $"\"{text}\"";
            return text;
        }
    }
}
