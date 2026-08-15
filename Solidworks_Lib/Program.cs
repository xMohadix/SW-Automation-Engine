/*
Programmer: Hadi Menzilcioglu

Contributors:
*Please add your name here if you contribute to this library*

Last Date Modified: 2026-08-15
Version: (Alpha) 1.3.0 / 1.0.0
SolidWorks Version Compatibility: SolidWorks 2020+ (64-bit)

Description:
An open-source .NET automation library for Dassault Systèmes SolidWorks.
Provides high-level APIs for CAD modeling, feature inspection, mass properties, 
BOM extraction, interference detection, and multi-axis clearance/tolerance validation.

License: MIT License
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidWorks_Lib
{
    #region SolidWorks Data Models (DTO Definitions)

    /// <summary>
    /// Data structure for storing geometric and dimensional metadata extracted from Hole Wizard features.
    /// </summary>
    public class HoleWizardData
    {
        // Feature Identification
        public string FeatureName { get; set; } = string.Empty;       // Feature Name (e.g., "M3 Tapped Hole1")
        public string HoleType { get; set; } = string.Empty;          // Hole Type (e.g., "Tap", "Counterbore", "Countersink", "Simple Hole")
        public string FastenerSize { get; set; } = string.Empty;      // Fastener Size Designation (e.g., "M3", "M2.5x0.45", "#4-40")
        public string Standard { get; set; } = string.Empty;          // Standard Name (e.g., "ISO", "DIN", "ANSI Metric")

        // Core Geometry (Unit: mm)
        public double HoleDiameter { get; set; }                      // Drill / Pilot Diameter (mm)
        public double HoleDepth { get; set; }                         // Total Hole Depth (mm)
        public double ThreadDepth { get; set; }                       // Threaded Depth (mm) - For tapped holes

        // Automatic Nominal Diameter Calculation (e.g., "M3" -> 3.0 mm, "M2.5x0.45" -> 2.5 mm)
        public double NominalDiameter
        {
            get
            {
                if (HoleDiameter > 0) return HoleDiameter;

                if (!string.IsNullOrEmpty(FastenerSize))
                {
                    string clean = FastenerSize.ToUpper().Replace("M", "").Split('X')[0].Trim();
                    if (double.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                    {
                        return val;
                    }
                }
                return 0.0;
            }
        }

        // End Conditions
        public bool IsThroughAll { get; set; }                        // Is Through All?
        public string EndCondition { get; set; } = string.Empty;      // "Through All", "Blind", etc.

        // Counterbore / Countersink Dimensions (if applicable)
        public double CounterBoreDiameter { get; set; }               // Counterbore Diameter (mm)
        public double CounterBoreDepth { get; set; }                  // Counterbore Depth (mm)
        public double CounterSinkDiameter { get; set; }               // Countersink Diameter (mm)
        public double CounterSinkAngle { get; set; }                  // Countersink Angle (degrees)

        // Script Analysis & DFM Flags
        public bool IsFlagged { get; set; } = false;                  // Flagged for DFM / non-standard check?
        public string FlagReason { get; set; } = string.Empty;        // Reason for warning or flag

        public override string ToString()
        {
            string depthStr = IsThroughAll ? "Through All" : $"{HoleDepth} mm";
            return $"[{FeatureName}] {HoleType} | Size: {FastenerSize} (Nominal: {NominalDiameter} mm) | Diameter: {HoleDiameter} mm | Depth: {depthStr}";
        }
    }

    /// <summary>
    /// Stores the result of a single-axis clearance and tolerance validation test.
    /// </summary>
    public class ClearanceResult
    {
        public string Direction { get; set; } = string.Empty;           // Direction label (e.g., "+X (Right)", "-Z (Down)")
        public string AxisName { get; set; } = string.Empty;            // Axis name ("X", "Y", "Z")
        public double ShiftAmount { get; set; }                         // Offset amount (mm, signed)
        public int InterferenceCount { get; set; }                      // Number of detected interferences
        public Dictionary<string, string> Interferences { get; set; }   // Interference details (Component pair -> Volume)
            = new Dictionary<string, string>();
        public bool HasInterference => InterferenceCount > 0;           // Indicates whether interferences were found

        public override string ToString()
        {
            string status = HasInterference ? $"COLLISION ({InterferenceCount} detected)" : "CLEAN";
            return $"[{Direction}] {Math.Abs(ShiftAmount):F2} mm offset -> {status}";
        }
    }

    #endregion

    /// <summary>
    /// Main SolidWorks automation engine implementing a Facade over the SolidWorks COM Interop API.
    /// </summary>
    public class SolidWorksEngine
    {
        // SolidWorks main application object
        public SldWorks? swApp;

        // Active document reference (Part, Assembly, etc.)
        public ModelDoc2? activeModel;

        #region SolidWorks Session & Administration Commands

        /// <summary>
        /// Starts a new SolidWorks application instance.
        /// </summary>
        /// <param name="head">True for visible GUI mode, False for invisible (headless/background) mode.</param>
        /// <returns>True if session started successfully, otherwise False.</returns>
        public bool Start_SW(bool head = false)
        {
            try
            {
                Type? swType = Type.GetTypeFromProgID("SldWorks.Application");
                if (swType == null)
                {
                    Console.WriteLine("Critical Error: SolidWorks is not registered in Windows COM (ProgID 'SldWorks.Application' not found).");
                    return false;
                }

                swApp = (SldWorks?)Activator.CreateInstance(swType);

                if (swApp == null)
                {
                    Console.WriteLine("Critical Error: Failed to create SolidWorks application instance.");
                    return false;
                }

                swApp.Visible = head;
                swApp.UserControl = head;

                if (head)
                {
                    Console.WriteLine("Launching SolidWorks in GUI mode...");
                }
                else
                {
                    Console.WriteLine("Launching SolidWorks in headless background mode...");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Could not start SolidWorks. Details: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Terminates the active SolidWorks session and safely releases COM memory.
        /// </summary>
        /// <returns>True if successfully closed, otherwise False.</returns>
        public bool Stop_SW()
        {
            try
            {
                if (swApp != null)
                {
                    swApp.ExitApp();
                    Marshal.ReleaseComObject(swApp);
                    swApp = null;
                    Console.WriteLine("SolidWorks session terminated successfully.");
                    return true;
                }
                else
                {
                    Console.WriteLine("SolidWorks session is already closed.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Could not close SolidWorks \n Details: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Displays an informational pop-up dialog in the SolidWorks UI. Blocks until dismissed.
        /// </summary>
        /// <param name="message">Message string to display</param>
        public void Write_Message(string message)
        {
            if (swApp != null)
            {
                swApp.SendMsgToUser(message);
            }
            else
            {
                Console.WriteLine("Active SolidWorks session not found.");
            }
        }

        /// <summary>
        /// Displays a Yes/No confirmation dialog in the SolidWorks UI.
        /// </summary>
        /// <param name="question">Question text to ask user</param>
        /// <returns>True if user clicks Yes, False if No.</returns>
        public bool Ask_Confirm(string question)
        {
            if (swApp != null)
            {
                int response = swApp.SendMsgToUser2(question, (int)swMessageBoxIcon_e.swMbQuestion, (int)swMessageBoxBtn_e.swMbYesNo);
                return response == (int)swMessageBoxResult_e.swMbHitYes;
            }
            else
            {
                Console.WriteLine("Active SolidWorks session not found.");
                return false;
            }
        }

        /// <summary>
        /// Attaches to and captures the currently open and active document in SolidWorks.
        /// </summary>
        /// <returns>True if active document was acquired, otherwise False.</returns>
        public bool Get_Active_Document()
        {
            if (swApp == null)
            {
                Console.WriteLine("Error: Active SolidWorks session not found. Please call Start_SW() first.");
                return false;
            }

            try
            {
                activeModel = (ModelDoc2?)swApp.ActiveDoc;

                if (activeModel != null)
                {
                    Console.WriteLine($"Active document acquired successfully: {activeModel.GetTitle()}");
                    return true;
                }
                else
                {
                    Console.WriteLine("Error: No document currently open in SolidWorks.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Exception occurred while retrieving active document: " + ex.Message);
                return false;
            }
        }

        #endregion

        #region SolidWorks Part Commands

        /// <summary>
        /// Creates a new Part (.sldprt) document using the user's default template.
        /// </summary>
        /// <returns>True if created successfully, otherwise False.</returns>
        public bool Create_New_Part()
        {
            if (swApp == null)
            {
                Console.WriteLine("Active SolidWorks session not found. Please call Start_SW() first.");
                return false;
            }

            try
            {
                string templatePath = swApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);

                if (string.IsNullOrEmpty(templatePath))
                {
                    Console.WriteLine("Error: Default Part template path is not set in SolidWorks preferences.");
                    return false;
                }

                activeModel = (ModelDoc2?)swApp.NewDocument(templatePath, 0, 0, 0);

                if (activeModel != null)
                {
                    Console.WriteLine("New Part document created successfully.");
                    return true;
                }
                else
                {
                    Console.WriteLine("Critical Error: Could not create Part document.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Exception occurred while creating Part: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Updates the unit measurement system for the active document.
        /// </summary>
        /// <param name="unitType">Unit designation: "mmgs", "ips", "mks", "cgs"</param>
        /// <returns>True if unit system updated successfully, otherwise False.</returns>
        public bool Change_Measure_System(string unitType)
        {
            if (activeModel == null)
            {
                Console.WriteLine("Error: No active model found! Create or open a document first.");
                return false;
            }

            int unitCode;

            switch (unitType.ToLower())
            {
                case "mmgs":
                case "mm":
                    unitCode = (int)swUnitSystem_e.swUnitSystem_MMGS;
                    break;
                case "ips":
                case "inch":
                    unitCode = (int)swUnitSystem_e.swUnitSystem_IPS;
                    break;
                case "mks":
                case "m":
                    unitCode = (int)swUnitSystem_e.swUnitSystem_MKS;
                    break;
                case "cgs":
                case "cm":
                    unitCode = (int)swUnitSystem_e.swUnitSystem_CGS;
                    break;
                default:
                    Console.WriteLine($"Warning: '{unitType}' is not recognized. Defaulting to MMGS.");
                    unitCode = (int)swUnitSystem_e.swUnitSystem_MMGS;
                    break;
            }

            try
            {
                activeModel.Extension.SetUserPreferenceInteger(
                    (int)swUserPreferenceIntegerValue_e.swUnitSystem,
                    0,
                    unitCode
                );

                Console.WriteLine($"Document unit system changed to '{unitType.ToUpper()}'.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Could not change unit system: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Saves the active Part document to disk.
        /// </summary>
        /// <param name="folderPath">Target directory path</param>
        /// <param name="fileName">File name (with or without .sldprt extension)</param>
        /// <param name="confirm">If true, prompts for user confirmation in SolidWorks UI before saving.</param>
        /// <returns>True if save succeeded, otherwise False.</returns>
        public bool Save_Part(string folderPath, string fileName, bool confirm)
        {
            if (activeModel == null)
            {
                Console.WriteLine("Error: No active model found to save!");
                return false;
            }

            if (!fileName.ToLower().EndsWith(".sldprt"))
            {
                fileName += ".sldprt";
            }

            string fullFilePath = Path.Combine(folderPath, fileName);

            if (confirm)
            {
                bool isApproved = Ask_Confirm($"Are you sure you want to save the part?\nFile: {fileName}");

                if (!isApproved)
                {
                    Console.WriteLine("Warning: Save operation was canceled by the user.");
                    return false;
                }
            }

            int errors = 0;
            int warnings = 0;

            try
            {
                bool status = activeModel.Extension.SaveAs(
                    fullFilePath,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null,
                    ref errors,
                    ref warnings
                );

                if (status)
                {
                    Console.WriteLine($"Part saved successfully!\nPath: {fullFilePath}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Critical Error: Failed to save part! API Error Code: {errors}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Exception occurred during save: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Calculates mass, volume, and surface area properties of the active model.
        /// </summary>
        /// <returns>Dictionary with keys: "Mass" (kg), "Volume" (m^3), "SurfaceArea" (m^2)</returns>
        public Dictionary<string, double> Get_Mass_Properties()
        {
            Dictionary<string, double> properties = new Dictionary<string, double>
            {
                { "Mass", 0.0 },
                { "Volume", 0.0 },
                { "SurfaceArea", 0.0 }
            };

            if (activeModel == null)
            {
                Console.WriteLine("Error: No active model found! Create or open a document first.");
                return properties;
            }

            try
            {
                ModelDocExtension swExt = activeModel.Extension;

                // Create MassProperty object (0: includes all bodies/components)
                MassProperty? massProp = (MassProperty?)swExt.CreateMassProperty();

                if (massProp != null)
                {
                    properties["Mass"] = massProp.Mass;                 // Kilograms (kg)
                    properties["Volume"] = massProp.Volume;             // Cubic meters (m^3)
                    properties["SurfaceArea"] = massProp.SurfaceArea;   // Square meters (m^2)

                    Console.WriteLine("Mass properties calculated successfully.");
                }
                else
                {
                    Console.WriteLine("Critical Error: Could not create MassProperty object.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Exception occurred while calculating mass properties: " + ex.Message);
            }

            return properties;
        }

        /// <summary>
        /// Traverses the Feature Manager design tree and extracts feature names and their types.
        /// </summary>
        /// <returns>Dictionary mapping Feature Name to Feature Type</returns>
        public Dictionary<string, string> Get_Feature_Tree()
        {
            Dictionary<string, string> featureTree = new Dictionary<string, string>();

            if (activeModel == null)
            {
                Console.WriteLine("Error: No active model found! Create or open a document first.");
                return featureTree;
            }

            // Verify document type (Part or Assembly)
            if (activeModel.GetType() != (int)swDocumentTypes_e.swDocPART && activeModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                Console.WriteLine("Info: Get_Feature_Tree is supported on Part and Assembly documents only.");
                return featureTree;
            }

            try
            {
                Feature? swFeat = (Feature?)activeModel.FirstFeature();

                if (swFeat == null)
                {
                    Console.WriteLine("Warning: No features found in Feature Manager tree.");
                    return featureTree;
                }

                while (swFeat != null)
                {
                    string featName = swFeat.Name;
                    string featType = swFeat.GetTypeName2();

                    if (!featureTree.ContainsKey(featName))
                    {
                        featureTree.Add(featName, featType);
                    }

                    swFeat = (Feature?)swFeat.GetNextFeature();
                }

                Console.WriteLine($"Success: {featureTree.Count} features read from Feature Tree.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Exception occurred while reading Feature Tree: " + ex.Message);
            }

            return featureTree;
        }

        /// <summary>
        /// Traverses the Feature Tree to identify parametric Fillet features and extracts their radius in millimeters (mm).
        /// </summary>
        /// <returns>Dictionary mapping Fillet Name to Radius Value (mm)</returns>
        public Dictionary<string, double> Inspect_Fillets()
        {
            Dictionary<string, double> filletData = new Dictionary<string, double>();

            if (activeModel == null)
            {
                Console.WriteLine("Error: No active model found! Create or open a document first.");
                return filletData;
            }

            if (activeModel.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                Console.WriteLine("Info: Fillet feature inspection is designed for Part documents.");
                return filletData;
            }

            try
            {
                Feature? swFeat = (Feature?)activeModel.FirstFeature();

                while (swFeat != null)
                {
                    if (swFeat.GetTypeName2() == "Fillet")
                    {
                        SimpleFilletFeatureData2? filletDef = (SimpleFilletFeatureData2?)swFeat.GetDefinition();

                        if (filletDef != null)
                        {
                            bool accessGranted = filletDef.AccessSelections(activeModel, null);

                            if (accessGranted)
                            {
                                // Convert API default meters (m) to millimeters (mm)
                                double radiusInMm = filletDef.DefaultRadius * 1000.0;

                                if (!filletData.ContainsKey(swFeat.Name))
                                {
                                    filletData.Add(swFeat.Name, Math.Round(radiusInMm, 2));
                                }

                                // Mandatory: Release selection lock to prevent resource leaks
                                filletDef.ReleaseSelectionAccess();
                            }
                        }
                    }

                    swFeat = (Feature?)swFeat.GetNextFeature();
                }

                Console.WriteLine($"Success: {filletData.Count} Fillet features analyzed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Exception occurred during fillet analysis: " + ex.Message);
            }

            return filletData;
        }

        /// <summary>
        /// Identifies all Hole Wizard ('HoleWzd') features in the Feature Tree and extracts structured DTO metadata.
        /// </summary>
        /// <returns>List of HoleWizardData objects</returns>
        public List<HoleWizardData> Inspect_Hole_Wizards()
        {
            List<HoleWizardData> holeList = new List<HoleWizardData>();

            if (activeModel == null)
            {
                Console.WriteLine("Error: No active model found! Please open a document first.");
                return holeList;
            }

            Dictionary<string, string> featureTree = Get_Feature_Tree();

            if (featureTree == null || featureTree.Count == 0)
            {
                Console.WriteLine("Warning: No features found in Feature Tree to analyze.");
                return holeList;
            }

            try
            {
                foreach (var item in featureTree)
                {
                    string featName = item.Key;
                    string featType = item.Value;

                    if (featType == "HoleWzd")
                    {
                        Feature? swFeat = (Feature?)((PartDoc)activeModel).FeatureByName(featName);
                        if (swFeat != null)
                        {
                            WizardHoleFeatureData2? holeData = (WizardHoleFeatureData2?)swFeat.GetDefinition();
                            if (holeData != null)
                            {
                                bool accessGranted = holeData.AccessSelections(activeModel, null);
                                if (accessGranted)
                                {
                                    HoleWizardData data = new HoleWizardData();
                                    data.FeatureName = featName;

                                    // Hole Type mapping
                                    switch (holeData.Type)
                                    {
                                        case 0: data.HoleType = "Counterbore"; break;
                                        case 1: data.HoleType = "Countersink"; break;
                                        case 2: data.HoleType = "Simple Hole"; break;
                                        case 3: data.HoleType = "Pipe Tap"; break;
                                        default:
                                            data.HoleType = featName.ToLower().Contains("tap") ? "Tap" : "Hole Wizard";
                                            break;
                                    }

                                    // Standard & Fastener Size
                                    data.FastenerSize = holeData.FastenerSize ?? string.Empty;
                                    data.Standard = holeData.Standard.ToString();

                                    // Diameters (mm)
                                    double rawDia = 0.0;
                                    if (holeData.TapDrillDiameter > 0) rawDia = holeData.TapDrillDiameter;
                                    else if (holeData.HoleDiameter > 0) rawDia = holeData.HoleDiameter;
                                    else if (holeData.Diameter > 0) rawDia = holeData.Diameter;
                                    else if (holeData.ThreadDiameter > 0) rawDia = holeData.ThreadDiameter;
                                    data.HoleDiameter = Math.Round(rawDia * 1000.0, 2);

                                    // Depths (mm)
                                    double rawDepth = 0.0;
                                    if (holeData.Depth > 0) rawDepth = holeData.Depth;
                                    else if (holeData.TapDrillDepth > 0) rawDepth = holeData.TapDrillDepth;
                                    else if (holeData.Length > 0) rawDepth = holeData.Length;
                                    data.HoleDepth = Math.Round(rawDepth * 1000.0, 2);

                                    // Thread Depth (mm)
                                    data.ThreadDepth = Math.Round(holeData.ThreadDepth * 1000.0, 2);

                                    // Counterbore / Countersink Dimensions (mm)
                                    data.CounterBoreDiameter = Math.Round(holeData.CounterBoreDiameter * 1000.0, 2);
                                    data.CounterBoreDepth = Math.Round(holeData.CounterBoreDepth * 1000.0, 2);
                                    data.CounterSinkDiameter = Math.Round(holeData.CounterSinkDiameter * 1000.0, 2);
                                    data.CounterSinkAngle = Math.Round(holeData.CounterSinkAngle * (180.0 / Math.PI), 2);

                                    // End Condition
                                    data.IsThroughAll = (holeData.EndCondition == (int)swEndConditions_e.swEndCondThroughAll || (rawDepth == 0));
                                    data.EndCondition = data.IsThroughAll ? "Through All" : "Blind";

                                    holeList.Add(data);

                                    holeData.ReleaseSelectionAccess();
                                }
                            }
                        }
                    }
                }

                Console.WriteLine($"Success: Extracted {holeList.Count} Hole Wizard features.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Exception occurred during Hole Wizard analysis: " + ex.Message);
            }

            return holeList;
        }

        #endregion

        #region SolidWorks Assembly Commands

        /// <summary>
        /// Retrieves all sub-component names from an active Assembly document.
        /// </summary>
        /// <returns>List of component names</returns>
        public List<string> Get_Components()
        {
            List<string> componentNames = new List<string>();

            if (activeModel == null)
            {
                Console.WriteLine("Error: No active model found! Please open a document first.");
                return componentNames;
            }

            if (activeModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                Console.WriteLine("Info: Active model is a Part document, not an Assembly.");
                componentNames.Add(activeModel.GetTitle());
                return componentNames;
            }

            try
            {
                AssemblyDoc asmDoc = (AssemblyDoc)activeModel;
                object[]? components = (object[]?)asmDoc.GetComponents(true); // true: includes all sub-components

                if (components != null)
                {
                    foreach (object compObj in components)
                    {
                        Component2? swComp = compObj as Component2;
                        if (swComp != null)
                        {
                            string compName = swComp.Name2;
                            if (!componentNames.Contains(compName))
                            {
                                componentNames.Add(compName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Exception occurred while reading components: " + ex.Message);
            }

            return componentNames;
        }

        /// <summary>
        /// Traverses assembly components and generates a Bill of Materials (BOM) with quantities.
        /// </summary>
        /// <returns>Dictionary mapping Component Name to Quantity</returns>
        public Dictionary<string, int> Generate_BOM()
        {
            Dictionary<string, int> bomDict = new Dictionary<string, int>();

            try
            {
                Dictionary<string, string> featureTree = Get_Feature_Tree();

                if (featureTree == null || featureTree.Count == 0)
                {
                    Console.WriteLine("Warning: No features found in tree to generate BOM.");
                    return bomDict;
                }

                foreach (var item in featureTree)
                {
                    string rawName = item.Key;

                    // Clean instance suffixes (e.g., "part1-1" -> "part1", "part3<2>" -> "part3", "part3^asm-1" -> "part3")
                    string baseName = rawName;

                    if (baseName.Contains("<"))
                    {
                        baseName = baseName.Split('<')[0];
                    }
                    if (baseName.Contains("^"))
                    {
                        baseName = baseName.Split('^')[0];
                    }
                    if (baseName.Contains("-"))
                    {
                        int lastDash = baseName.LastIndexOf('-');
                        if (lastDash > 0)
                        {
                            baseName = baseName.Substring(0, lastDash);
                        }
                    }

                    baseName = baseName.Trim();

                    if (string.IsNullOrEmpty(baseName)) continue;

                    if (bomDict.ContainsKey(baseName))
                    {
                        bomDict[baseName]++;
                    }
                    else
                    {
                        bomDict[baseName] = 1;
                    }
                }

                Console.WriteLine("\n--- BOM (Bill of Materials) ---");
                foreach (var kvp in bomDict)
                {
                    Console.WriteLine($"{kvp.Key}:{kvp.Value}");
                }
                Console.WriteLine("-------------------------------\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Exception occurred during BOM generation: " + ex.Message);
            }

            return bomDict;
        }

        /// <summary>
        /// Detects if a component is a standard screw, bolt, nut, or fastener by name or file path.
        /// </summary>
        private bool Is_Screw_Or_Fastener(Component2? comp, string compName)
        {
            if (comp == null && string.IsNullOrEmpty(compName)) return false;

            // 1. Standard naming pattern check
            if (!string.IsNullOrEmpty(compName))
            {
                string lowerName = compName.ToLower();
                string[] screwKeywords = new string[] {
                    "screw", "bolt", "din-", "din_", "din ", "iso-", "iso_", "iso ",
                    "asme", "ansi", "nut", "washer", "fastener",
                    "recess", "pan head", "hex head", "socket head"
                };

                foreach (string kw in screwKeywords)
                {
                    if (lowerName.Contains(kw)) return true;
                }
            }

            // 2. Toolbox directory path check
            try
            {
                if (comp != null)
                {
                    string path = comp.GetPathName();
                    if (!string.IsNullOrEmpty(path))
                    {
                        string lowerPath = path.ToLower();
                        if (lowerPath.Contains("solidworks data") || lowerPath.Contains("toolbox") || lowerPath.Contains("browser"))
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Detects physical solid body collisions in an active Assembly document.
        /// </summary>
        /// <param name="treatCoincidence">If true, surface contacts are counted as interferences (default: false).</param>
        /// <param name="include_Screws">If false, standard fasteners and hardware are excluded (default: true).</param>
        /// <returns>Dictionary mapping Component Pair to Collision Volume</returns>
        public Dictionary<string, string> Get_Interferences(bool treatCoincidence = false, bool include_Screws = true)
        {
            Dictionary<string, string> interferenceDict = new Dictionary<string, string>();

            if (activeModel == null)
            {
                Console.WriteLine("Error: No active model found! Please open an assembly first.");
                return interferenceDict;
            }

            if (activeModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                Console.WriteLine("Info: Interference analysis is supported on Assembly documents only.");
                return interferenceDict;
            }

            try
            {
                AssemblyDoc asmDoc = (AssemblyDoc)activeModel;
                InterferenceDetectionMgr? intMgr = asmDoc.InterferenceDetectionManager;

                if (intMgr == null)
                {
                    Console.WriteLine("Error: Could not obtain InterferenceDetectionManager instance.");
                    return interferenceDict;
                }

                // 1. Configure detection options
                intMgr.TreatCoincidenceAsInterference = treatCoincidence;
                intMgr.TreatSubAssembliesAsComponents = true;
                intMgr.IgnoreHiddenBodies = true;

                // 2. Compute interferences
                object[]? interferences = (object[]?)intMgr.GetInterferences();

                if (interferences == null || interferences.Length == 0)
                {
                    Console.WriteLine("Info: No interferences detected in the assembly.");
                    intMgr.Done();
                    return interferenceDict;
                }

                int totalCount = interferences.Length;
                int index = 1;
                int filteredCount = 0;

                Console.WriteLine($"\n--- INTERFERENCE ANALYSIS (Raw Detections: {totalCount}) ---");
                if (!include_Screws) Console.WriteLine("Filter: Fasteners and hardware are excluded from results...");

                foreach (object item in interferences)
                {
                    IInterference intItem = (IInterference)item;
                    object[]? components = (object[]?)intItem.Components;

                    if (components != null && components.Length >= 2)
                    {
                        Component2? comp1 = (Component2?)components[0];
                        Component2? comp2 = (Component2?)components[1];

                        string comp1Name = comp1 != null ? comp1.Name2 : "UnknownComponent1";
                        string comp2Name = comp2 != null ? comp2.Name2 : "UnknownComponent2";

                        if (!include_Screws)
                        {
                            bool isComp1Screw = Is_Screw_Or_Fastener(comp1, comp1Name);
                            bool isComp2Screw = Is_Screw_Or_Fastener(comp2, comp2Name);

                            if (isComp1Screw || isComp2Screw)
                            {
                                continue;
                            }
                        }

                        double volume_mm3 = intItem.Volume * 1e9; // m^3 -> mm^3
                        string locationStr = $"Interference Volume: {volume_mm3:F2} mm³";

                        string pairKey = $"{comp1Name} - {comp2Name}";
                        if (interferenceDict.ContainsKey(pairKey))
                        {
                            pairKey = $"{comp1Name} - {comp2Name} (#{index})";
                        }

                        interferenceDict.Add(pairKey, locationStr);
                        filteredCount++;

                        Console.WriteLine($"[{filteredCount}] {pairKey}: {locationStr}");
                        index++;
                    }
                }

                Console.WriteLine($"\nSummary: {filteredCount} mechanical collisions listed ({(totalCount - filteredCount)} fasteners filtered).");
                Console.WriteLine("----------------------------------------------------------\n");

                intMgr.Done();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Exception occurred during interference detection: " + ex.Message);
            }

            return interferenceDict;
        }

        /// <summary>
        /// Filters reciprocal duplicate collisions (e.g., "A - B" vs "B - A") so each component pair appears once.
        /// </summary>
        /// <param name="collisionDict">Raw interference dictionary</param>
        /// <returns>Deduplicated dictionary</returns>
        public Dictionary<string, string> Eliminate_Duplicate_Collisions(Dictionary<string, string> collisionDict)
        {
            Dictionary<string, string> filteredDict = new Dictionary<string, string>();
            HashSet<string> seenPairs = new HashSet<string>();

            foreach (var kvp in collisionDict)
            {
                string key = kvp.Key;

                // 1. Remove index numbers (e.g., "Part1 - Part2 (#1)" -> "Part1 - Part2")
                string cleanKey = System.Text.RegularExpressions.Regex.Replace(key, @"\s*\(\#\d+\)$", "");

                // 2. Canonical pair sorting
                string uniqueId = cleanKey;
                int sepIndex = cleanKey.IndexOf(" - ");

                if (sepIndex > 0)
                {
                    string p1 = cleanKey.Substring(0, sepIndex);
                    string p2 = cleanKey.Substring(sepIndex + 3);

                    if (string.Compare(p1, p2, StringComparison.Ordinal) > 0)
                    {
                        uniqueId = $"{p2} - {p1}";
                    }
                }

                // 3. Add if not seen before
                if (!seenPairs.Contains(uniqueId))
                {
                    seenPairs.Add(uniqueId);
                    filteredDict.Add(key, kvp.Value);
                }
            }

            return filteredDict;
        }

        /// <summary>
        /// Temporarily shifts a target component by (deltaX, deltaY, deltaZ) mm, performs clearance/interference analysis,
        /// and restores the component and its mates to their original state.
        /// </summary>
        /// <param name="componentName">Target component name</param>
        /// <param name="deltaX">X offset in mm</param>
        /// <param name="deltaY">Y offset in mm</param>
        /// <param name="deltaZ">Z offset in mm</param>
        /// <param name="treatCoincidence">Treat surface contacts as collisions</param>
        /// <param name="include_Screws">Include fasteners</param>
        /// <returns>Dictionary of detected interferences in displaced position</returns>
        public Dictionary<string, string> Shift_Component_And_Analyze_Clearance(string componentName, double deltaX, double deltaY, double deltaZ, bool treatCoincidence = false, bool include_Screws = false)
        {
            Dictionary<string, string> resultDict = new Dictionary<string, string>();

            if (activeModel == null)
            {
                Console.WriteLine("Error: No active model found.");
                return resultDict;
            }

            AssemblyDoc? swAssy = activeModel as AssemblyDoc;
            if (swAssy == null)
            {
                Console.WriteLine("Error: Active model is not an Assembly document.");
                return resultDict;
            }

            // 1. Locate target component (supports partial name matching)
            Component2? targetComponent = swAssy.GetComponentByName(componentName);

            if (targetComponent == null)
            {
                object[]? components = (object[]?)swAssy.GetComponents(false);
                if (components != null)
                {
                    foreach (object compObj in components)
                    {
                        Component2 comp = (Component2)compObj;
                        if (comp.Name2.Contains(componentName))
                        {
                            targetComponent = comp;
                            Console.WriteLine($"Info: Exact name not matched, selected matching component -> {targetComponent.Name2}");
                            break;
                        }
                    }
                }
            }

            if (targetComponent == null)
            {
                Console.WriteLine($"Error: Component matching '{componentName}' not found in assembly.");
                return resultDict;
            }

            // 2. Find and suppress all mates attached to target component
            List<Feature> suppressedMates = new List<Feature>();
            Feature? swFeat = (Feature?)activeModel.FirstFeature();

            Console.WriteLine($"\nLocating and suppressing mates for component '{componentName}'...");
            while (swFeat != null)
            {
                if (swFeat.GetTypeName2() == "MateGroup")
                {
                    Feature? swSubFeat = (Feature?)swFeat.GetFirstSubFeature();
                    while (swSubFeat != null)
                    {
                        if (swSubFeat.GetTypeName2() == "Mate")
                        {
                            Mate2? swMate = (Mate2?)swSubFeat.GetSpecificFeature2();
                            if (swMate != null)
                            {
                                int entityCount = swMate.GetMateEntityCount();
                                bool isRelated = false;
                                for (int i = 0; i < entityCount; i++)
                                {
                                    MateEntity2? swMateEnt = swMate.MateEntity(i);
                                    if (swMateEnt != null)
                                    {
                                        Component2? refComp = swMateEnt.ReferenceComponent;
                                        if (refComp != null && refComp.Name2 == targetComponent.Name2)
                                        {
                                            isRelated = true;
                                            break;
                                        }
                                    }
                                }

                                if (isRelated)
                                {
                                    bool suppressed = swSubFeat.SetSuppression2(0, 1, null);
                                    if (suppressed)
                                    {
                                        suppressedMates.Add(swSubFeat);
                                    }
                                }
                            }
                        }
                        swSubFeat = (Feature?)swSubFeat.GetNextSubFeature();
                    }
                }
                swFeat = (Feature?)swFeat.GetNextFeature();
            }

            Console.WriteLine($"Total {suppressedMates.Count} mate(s) temporarily suppressed.");

            // 3. Capture original transformation matrix
            MathTransform originalTransform = targetComponent.Transform2;

            try
            {
                // 4. Calculate and apply translated transform matrix (convert mm to meters)
                if (swApp == null)
                {
                    Console.WriteLine("Error: SolidWorks application reference is null.");
                    return resultDict;
                }

                MathUtility? mathUtil = (MathUtility?)swApp.GetMathUtility();
                if (mathUtil == null)
                {
                    Console.WriteLine("Error: Could not obtain MathUtility.");
                    return resultDict;
                }

                double[] transformData = (double[])originalTransform.ArrayData;

                transformData[9] += deltaX / 1000.0;
                transformData[10] += deltaY / 1000.0;
                transformData[11] += deltaZ / 1000.0;

                MathTransform newTransform = (MathTransform)mathUtil.CreateTransform(transformData);
                targetComponent.Transform2 = newTransform;

                activeModel.GraphicsRedraw2();

                Console.WriteLine($"Component displaced: X({deltaX}mm), Y({deltaY}mm), Z({deltaZ}mm)");
                Console.WriteLine("Running clearance and interference detection...");

                // 5. Evaluate interferences in shifted position
                Dictionary<string, string> rawCollisions = Get_Interferences(treatCoincidence, include_Screws);
                resultDict = rawCollisions;

                Console.WriteLine($"Detected {resultDict.Count} raw collision(s) in displaced state.");
            }
            finally
            {
                // 6. Restore original transformation matrix
                targetComponent.Transform2 = originalTransform;

                // 7. Unsuppress mates
                foreach (Feature mate in suppressedMates)
                {
                    mate.SetSuppression2(1, 1, null);
                }

                Console.WriteLine("Component restored to original position and mates unsuppressed.");

                // 8. Rebuild assembly
                activeModel.EditRebuild3();
            }

            return resultDict;
        }

        /// <summary>
        /// Performs an automated clearance sweep on a component across 6 independent spatial directions (+X, -X, +Y, -Y, +Z, -Z).
        /// Displaces component independently in each direction, checks collisions, and returns structured ClearanceResult items.
        /// </summary>
        /// <param name="componentName">Target component name</param>
        /// <param name="tolerance_mm">Offset tolerance distance (mm)</param>
        /// <param name="axesToTest">Specific direction codes to test (e.g. ["+X", "-Y"]). Null for all 6 axes.</param>
        /// <param name="treatCoincidence">Treat surface contacts as collisions</param>
        /// <param name="include_Screws">Include fasteners</param>
        /// <returns>List of ClearanceResult reports per tested direction</returns>
        public List<ClearanceResult> Analyze_Clearance_All_Axes(string componentName, double tolerance_mm, List<string>? axesToTest = null, bool treatCoincidence = false, bool include_Screws = false)
        {
            List<ClearanceResult> results = new List<ClearanceResult>();

            if (activeModel == null)
            {
                Console.WriteLine("Error: No active model found.");
                return results;
            }

            if (tolerance_mm <= 0)
            {
                Console.WriteLine("Error: Tolerance distance must be a positive number.");
                return results;
            }

            // 6 standard independent spatial directions
            var allDirections = new (string label, string axis, string code, double dX, double dY, double dZ)[]
            {
                ("+X (Right)",    "X", "+X",  tolerance_mm,  0,             0),
                ("-X (Left)",     "X", "-X", -tolerance_mm,  0,             0),
                ("+Y (Forward)",  "Y", "+Y",  0,             tolerance_mm,  0),
                ("-Y (Backward)", "Y", "-Y",  0,            -tolerance_mm,  0),
                ("+Z (Up)",       "Z", "+Z",  0,             0,             tolerance_mm),
                ("-Z (Down)",     "Z", "-Z",  0,             0,            -tolerance_mm)
            };

            var directionsToTest = new List<(string label, string axis, string code, double dX, double dY, double dZ)>();

            if (axesToTest != null && axesToTest.Count > 0)
            {
                foreach (var dir in allDirections)
                {
                    if (axesToTest.Exists(a => a.ToUpper() == dir.code))
                    {
                        directionsToTest.Add(dir);
                    }
                }
            }
            else
            {
                directionsToTest.AddRange(allDirections);
            }

            if (directionsToTest.Count == 0)
            {
                Console.WriteLine("Error: No valid test directions specified.");
                return results;
            }

            Console.WriteLine($"\n==================================================");
            Console.WriteLine($"   MULTI-AXIS CLEARANCE & TOLERANCE ANALYSIS");
            Console.WriteLine($"   Component: {componentName}");
            Console.WriteLine($"   Directions: {string.Join(", ", directionsToTest.Select(d => d.code))}");
            Console.WriteLine($"   Tolerance: {tolerance_mm} mm (tested independently)");
            Console.WriteLine($"==================================================\n");

            int testNo = 1;
            foreach (var dir in directionsToTest)
            {
                Console.WriteLine($"\n--- [{testNo}/{directionsToTest.Count}] Direction: {dir.label} ---");

                ClearanceResult result = new ClearanceResult
                {
                    Direction = dir.label,
                    AxisName = dir.axis,
                    ShiftAmount = (dir.dX != 0) ? dir.dX : (dir.dY != 0) ? dir.dY : dir.dZ
                };

                try
                {
                    Dictionary<string, string> rawCollisions = Shift_Component_And_Analyze_Clearance(
                        componentName: componentName,
                        deltaX: dir.dX,
                        deltaY: dir.dY,
                        deltaZ: dir.dZ,
                        treatCoincidence: treatCoincidence,
                        include_Screws: include_Screws
                    );

                    Dictionary<string, string> cleanCollisions = Eliminate_Duplicate_Collisions(rawCollisions);

                    result.Interferences = cleanCollisions;
                    result.InterferenceCount = cleanCollisions.Count;

                    if (result.HasInterference)
                    {
                        Console.WriteLine($"⚠ {dir.label}: {result.InterferenceCount} collision(s) detected!");
                        foreach (var kvp in cleanCollisions)
                        {
                            Console.WriteLine($"   → {kvp.Key}: {kvp.Value}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"✓ {dir.label}: Clean — {tolerance_mm} mm clearance verified.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: Test failed in direction {dir.label}: {ex.Message}");
                    result.InterferenceCount = -1;
                }

                results.Add(result);
                testNo++;
            }

            // Summary Table
            Console.WriteLine($"\n==================================================");
            Console.WriteLine($"   CLEARANCE SUMMARY REPORT");
            Console.WriteLine($"==================================================");
            Console.WriteLine($"   {"Direction",-18} {"Offset",-12} {"Result",-10} {"Collisions"}");
            Console.WriteLine($"   {new string('-', 56)}");

            foreach (var r in results)
            {
                string status = r.InterferenceCount < 0 ? "ERROR" : (r.HasInterference ? "COLLISION" : "CLEAN");
                string countStr = r.InterferenceCount < 0 ? "-" : r.InterferenceCount.ToString();
                Console.WriteLine($"   {r.Direction,-18} {r.ShiftAmount,+8:F2} mm   {status,-10} {countStr}");
            }

            int totalProblems = results.Count(r => r.HasInterference);
            Console.WriteLine($"\n   Result: {totalProblems} of {results.Count} directions exhibited clearance collisions.");
            Console.WriteLine($"==================================================\n");

            return results;
        }

        #endregion

        #region SolidWorks File & Export Commands

        /// <summary>
        /// Selects a specified feature, focuses the camera on it (ZoomToSelection), and exports a JPG screenshot.
        /// Requires SolidWorks to be running in GUI mode (head = true).
        /// </summary>
        /// <param name="featureName">Name of the target feature</param>
        /// <param name="exportFolderPath">Export directory path</param>
        /// <returns>True if screenshot was saved successfully, otherwise False.</returns>
        public bool Take_Feature_Screenshot(string featureName, string exportFolderPath)
        {
            if (activeModel == null)
            {
                Console.WriteLine("Error: No active model found.");
                return false;
            }

            try
            {
                string filePath = Path.Combine(exportFolderPath, $"{featureName}.jpg");

                // 1. Clear previous selections
                activeModel.ClearSelection2(true);

                // 2. Select feature by name
                bool isSelected = activeModel.Extension.SelectByID2(featureName, "BODYFEATURE", 0, 0, 0, false, 0, null, 0);

                if (!isSelected)
                {
                    Console.WriteLine($"Warning: Could not select feature '{featureName}', skipping screenshot.");
                    return false;
                }

                // 3. Zoom to selection
                activeModel.ViewZoomToSelection();

                // 4. Save viewport image as JPG
                int errors = 0;
                int warnings = 0;
                bool isSaved = activeModel.Extension.SaveAs(
                    filePath,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null,
                    ref errors,
                    ref warnings
                );

                // 5. Reset camera and clear selection
                activeModel.ViewZoomtofit2();
                activeModel.ClearSelection2(true);

                if (isSaved)
                {
                    Console.WriteLine($"Screenshot saved: {featureName}.jpg");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Error: Failed to save screenshot! API Error Code: {errors}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical Error: Exception occurred while capturing screenshot for '{featureName}': " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Creates a directory with the specified name in the application working folder if it doesn't already exist.
        /// </summary>
        /// <param name="folderName">Folder name</param>
        /// <returns>Full directory path</returns>
        public string Create_Part_Folder(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
            {
                folderName = "SW_Parts";
                Console.WriteLine($"Folder name was empty. Defaulting to: {folderName}");
            }

            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string fullPath = Path.Combine(basePath, folderName);

            try
            {
                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                    Console.WriteLine($"Folder created successfully!\nPath: {fullPath}");
                }
                else
                {
                    Console.WriteLine($"Info: Folder '{folderName}' already exists.");
                }

                return fullPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Failed to create folder. Details: " + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Opens a SolidWorks document (.sldprt, .sldasm, .slddrw) from disk and assigns it as the active model.
        /// </summary>
        /// <param name="filePath">Full path to CAD file</param>
        /// <returns>True if opened successfully, otherwise False.</returns>
        public bool Open_Document(string filePath)
        {
            if (swApp == null)
            {
                Console.WriteLine("Active SolidWorks session not found. Please call Start_SW() first.");
                return false;
            }

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: Specified file does not exist!\nPath: {filePath}");
                return false;
            }

            int docType = (int)swDocumentTypes_e.swDocNONE;
            string ext = Path.GetExtension(filePath).ToLower();

            if (ext == ".sldprt")
            {
                docType = (int)swDocumentTypes_e.swDocPART;
            }
            else if (ext == ".sldasm")
            {
                docType = (int)swDocumentTypes_e.swDocASSEMBLY;
            }
            else if (ext == ".slddrw")
            {
                docType = (int)swDocumentTypes_e.swDocDRAWING;
            }
            else
            {
                Console.WriteLine("Error: Unsupported SolidWorks file extension!");
                return false;
            }

            int errors = 0;
            int warnings = 0;

            try
            {
                activeModel = (ModelDoc2?)swApp.OpenDoc6(
                    filePath,
                    docType,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings
                );

                if (activeModel != null)
                {
                    Console.WriteLine($"Document opened successfully: {Path.GetFileName(filePath)}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Critical Error: Could not open document! API Error Code: {errors}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: Exception occurred while opening document: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Exports key-value dictionary data to standard CSV format compatible with Python, Pandas, and Excel.
        /// </summary>
        /// <typeparam name="TKey">Dictionary key type</typeparam>
        /// <typeparam name="TValue">Dictionary value type</typeparam>
        /// <param name="data">Data dictionary</param>
        /// <param name="filePath">Target CSV file path</param>
        /// <returns>True if exported successfully, otherwise False.</returns>
        public bool Export_Dict_To_Csv<TKey, TValue>(Dictionary<TKey, TValue> data, string filePath) where TKey : notnull
        {
            try
            {
                var lines = new List<string> { "Key,Value" };
                lines.AddRange(data.Select(x => $"{x.Key},{x.Value}"));
                File.WriteAllLines(filePath, lines, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("CSV Export Error: " + ex.Message);
                return false;
            }
        }

        #endregion
    }
}