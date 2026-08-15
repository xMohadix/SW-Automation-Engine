using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks_Lib;

namespace ClearanceAnalysis
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Starting ClearanceAnalysis (Multi-Axis Clearance Test) Script...");
            Console.WriteLine("-----------------------------------------------------------------");

            // 1. Retrieve File Path
            string? filePath;
            if (args.Length > 0)
            {
                filePath = args[0];
            }
            else
            {
                Console.Write("Please enter the full path of the SolidWorks assembly file to analyze: ");
                filePath = Console.ReadLine();
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Console.WriteLine("Error: Valid file path was not provided or file not found.");
                return;
            }

            // 2. Retrieve Target Component Name and Tolerance Value
            Console.Write("Enter the name of the component to translate for clearance testing (Component Name): ");
            string? componentName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(componentName))
            {
                Console.WriteLine("Error: Component name cannot be empty.");
                return;
            }

            Console.Write("Please enter the clearance/tolerance distance to test in millimeters (mm) (e.g., 0.5): ");
            if (!double.TryParse(Console.ReadLine(), out double tolerance))
            {
                Console.WriteLine("Error: Invalid numeric tolerance value provided.");
                return;
            }

            // Determine axes to test
            List<string> axesToTest = new List<string>();

            Console.Write("Test along X axis? (+X and -X) (Y/N) [Default: Y]: ");
            string? xInput = Console.ReadLine()?.Trim().ToUpper();
            if (xInput != "N" && xInput != "H") { axesToTest.Add("+X"); axesToTest.Add("-X"); }

            Console.Write("Test along Y axis? (+Y and -Y) (Y/N) [Default: Y]: ");
            string? yInput = Console.ReadLine()?.Trim().ToUpper();
            if (yInput != "N" && yInput != "H") { axesToTest.Add("+Y"); axesToTest.Add("-Y"); }

            Console.Write("Test along Z axis? (+Z and -Z) (Y/N) [Default: Y]: ");
            string? zInput = Console.ReadLine()?.Trim().ToUpper();
            if (zInput != "N" && zInput != "H") { axesToTest.Add("+Z"); axesToTest.Add("-Z"); }

            if (axesToTest.Count == 0)
            {
                Console.WriteLine("Error: No axes selected for testing.");
                return;
            }

            // 3. Initialize SolidWorksEngine (Headless mode = false)
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
                // 4. Open Assembly Document
                Console.WriteLine($"Opening assembly: {filePath}");
                bool isOpened = swEngine.Open_Document(filePath);

                if (!isOpened)
                {
                    Console.WriteLine("Error: Could not open document.");
                    return;
                }

                // 5. Run Multi-Axis Clearance Analysis (Analyze_Clearance_All_Axes)
                Console.WriteLine($"\nTesting component '{componentName}' sequentially along selected axes (forward and backward)...");

                List<ClearanceResult> results = swEngine.Analyze_Clearance_All_Axes(
                    componentName,
                    tolerance,
                    axesToTest,
                    treatCoincidence: false,
                    include_Screws: false
                );

                if (results == null || results.Count == 0)
                {
                    Console.WriteLine("Could not perform clearance analysis.");
                }
                else
                {
                    Console.WriteLine("\n--- ANALYSIS RESULTS ---");
                    foreach (var res in results)
                    {
                        if (res.HasInterference)
                        {
                            Console.WriteLine($"[FAILED]  Direction {res.Direction} ({res.ShiftAmount} mm): {res.InterferenceCount} collision(s) detected.");
                        }
                        else
                        {
                            Console.WriteLine($"[PASSED]  Direction {res.Direction}: Clear. No collisions detected.");
                        }
                    }

                    // 6. Export Detailed Results to CSV
                    string baseFolder = Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    string csvPath = Path.Combine(baseFolder, "Clearance_Detailed_Report.csv");
                    ExportDetailedResults(results, csvPath);

                    Console.WriteLine($"\nDetailed report saved to: {csvPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
            finally
            {
                // 7. Close SolidWorks
                Console.WriteLine("\nClosing SolidWorks session...");
                swEngine.Stop_SW();
                Console.WriteLine("Process complete. Press any key to exit...");
                Console.ReadKey();
            }
        }

        // Helper function to export List<ClearanceResult> to a detailed CSV format
        static void ExportDetailedResults(List<ClearanceResult> results, string filePath)
        {
            if (results == null || results.Count == 0) return;

            List<string> csvLines = new List<string>();
            csvLines.Add("Direction,Axis,ShiftAmount_mm,Status,InterferenceCount,CollidingParts,IntersectionVolume_mm3");

            foreach (var res in results)
            {
                if (!res.HasInterference || res.Interferences == null || res.Interferences.Count == 0)
                {
                    csvLines.Add($"{res.Direction},{res.AxisName},{res.ShiftAmount},PASSED,0,,");
                }
                else
                {
                    foreach (var collision in res.Interferences)
                    {
                        // collision.Key = Colliding component pair, collision.Value = Volume
                        csvLines.Add($"{res.Direction},{res.AxisName},{res.ShiftAmount},COLLISION,{res.InterferenceCount},\"{collision.Key}\",\"{collision.Value}\"");
                    }
                }
            }

            File.WriteAllLines(filePath, csvLines);
        }
    }
}
