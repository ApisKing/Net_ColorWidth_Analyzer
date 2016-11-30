using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace Net_ColorWidth_Analyzer
{
    //version 1.0 By Apis, 2014Oct15: Basic Function
    //version 1.1 By Apis, 2014Oct20: Add tie Net
    //version 1.2 By Apis, 2014Oct30: Modify the Tie Net Reg
    //version 2.0 By Apis, 2015Nov03: Add Reading dialcnet.dat
    //version 2.1 By Apis, 2015Dec12: Modify key note Bug
    //version 2.2 By Apis, 2015Dec17: Add Highest Priority CW check
    //version 3.0 By Apis, 2016Nov20: Change to Cadence 16.6

    //To Do: Find a way to assure the single pin net

    class Program
    {
        //Regex string
        //wire 
        const string strPattern_Wire = @"^WIRE\s(\d+)\s(-?\d+)\s\((-?\d+\s-?\d+)\)\((-?\d+\s-?\d+)\)";
        //net
        const string strPattern_Net = @"^FORCEPROP.*LAST\sSIG_NAME\s(.+)$";
        //PinNet(Global & Unnamed net)
        const string strPattern_PinNet = @"^FORCEPROP.*LASTPIN\s\((-?\d+\s-?\d+)\)\sSIG_NAME\s(.+)$";
        //16.3 version
        //Tie
        //const string strPattern_Tie = @"^\s+assign\s+\\?(page\d+_)?([a-z0-9A-Z_\+\-\*]+)\s+=\s+(glbl\.\\?)?\\?([a-z0-9A-Z_\+\-\*]+)\s*;$";
        //16.6 version
        //XML_NetStart
        const string strPattern_XML_NetStart = @"^\s+<net>$";
        //XML_NetNumber
        const string strPattern_XML_NetNumber = @"^\s+<id>(\w+)<\/id>$";
        //XML_NetName
        const string strPattern_XML_NetName = @"^\s+<name>(?:page\d+_)?([a-z0-9A-Z_\+\-\.\*]+)<\/name>$";
        //XML_NetEnd
        const string strPattern_XML_NetEnd = @"^\s+<\/net>$";
        //XML_AliasesStart
        const string strPattern_XML_AliasesStart = @"^\s+<aliases>$";
        //XML_AliasesNet
        const string strPattern_XML_AliasesNet = @"^\s+<alias\snet1=""(\w+)"".+net2=""(\w+)"".+\/>$";
        //XML_AliasesEnd
        const string strPattern_XML_AliasesEnd = @"^\s+<\/aliases>$";

        //Dictionary
        //Global var: number=>Color_Thick        
        public static Dictionary<string, string> dctNumber_ColorWidth = new Dictionary<string, string>();
        //Global var: Net=>Color_Thick(First Priority)       
        public static Dictionary<string, string> dctNet_ColorWidth = new Dictionary<string, string>();
        //Global var: Net=>Color_Thick_List     
        public static Dictionary<string, List<string>> dctNet_lstColorWidth = new Dictionary<string, List<string>>();
        //Global var: fake=>true Net   
        public static Dictionary<string, string> dctShortNet_BasicNet = new Dictionary<string, string>();
        //Global var: SinlePinNet=>Value:fake Net rank highest priority in the Tie;   
        public static Dictionary<string, string> dctFakeShortNet = new Dictionary<string, string>();
        //Global var: Net=>Color_Thick(First Priority in the ShortNets)       
        public static Dictionary<string, string> dctNet_ShortColorWidth = new Dictionary<string, string>();
        //Globa1 var: NetNumber=>Net
        public static Dictionary<string, string> dctNetNumber_Net = new Dictionary<string, string>();

        //List
        //List to store net has mutiple color
        public static List<string> lstMultiCWnet = new List<string>();
        //List to store the priority of Color_Width
        public static List<string> lstCWPri = new List<string>();
        //List to store all the true net
        public static List<string> lstDialcNet = new List<string>();
        //List for all pages
        public static List<string> lstPage = new List<string>();
        //List to store all the fake net that short to the dialcenet
        public static List<string> lstShortNet = new List<string>();
        //List to store all the Single Pin Net 
        public static List<string> lstSinglePinNet = new List<string>();
        //List to store NetNumber
        public static List<string> lstNetNumber = new List<string>();
        //List to store NetNumber
        public static List<string> lstNet = new List<string>();

        //To Delete
        //Global var: Net=>Color_Thick(First Priority, Second Priority......), Single Net-ColorWidth not include         
        public static Dictionary<string, List<string>> dctDumpNetColorWidth = new Dictionary<string, List<string>>();
        //Global var: Key: Net rank highest priority; Value: Net Tie Together
        public static Dictionary<string, List<string>> dctTieNetList = new Dictionary<string, List<string>>();

        //Global var: store Result list: Net, ColorWidth
        public static List<string[]> lstNetColorWidth = new List<string[]>();
        //Global var: CW = > Priority
        public static Dictionary<string, int> dctCW_Pri = new Dictionary<string, int>();


        //Main
        static void Main(string[] args)
        {

            //read input from Consle
            Console.WriteLine("Please enter page number range to analyze:");

            GetPageList(Console.ReadLine());

            Console.WriteLine("Initialization...");

            // Create Log file to record errors
            StreamWriter warningLog = new StreamWriter("Warning.log", false);
            warningLog.Close();

            //Set up Number=>ColorWidth
            InitialCWTable();

            //Read the input file of Color_Width Priority
            ReadInputCW_Pri();

            Console.WriteLine("Analyzing sch csa file...");
            // Analyze page by page
            lstPage.ForEach(PageAnalyze);

            Console.WriteLine("Analyzing dialcnet.dat...");
            //Read the dialcnet.dat
            //Assign the net not colored as DialcNet
            ReadDialcnet();

            Console.WriteLine("Analyzing verilog.v...");
            // Analyze the verilog.v file to replace the tied net
            ReadVerilog();

            // Update for all the net
            //foreach (KeyValuePair<string, string> kvp in dctNet_ColorWidth)
            //{
            //    if (dctTieNet.ContainsKey(kvp.Key))
            //        dctNet_lstColorWidth.Add(kvp.Key, dctNet_ColorWidth[dctTieNet[kvp.Key]]);
            //    else
            //        dctNet_lstColorWidth.Add(kvp.Key, kvp.Value);
            //}

            Console.WriteLine("Generate Report...");
            // Generate report
            GenerateReport();

            Console.WriteLine("Task finished. Over!");
        }


        //Analyze every page
        private static void PageAnalyze(string s)
        {
            Boolean blnWireFound = false;   //normal net is just behind the Wire, use this flage to conect Net to Point
            string strColorWidth = null;    //Wire Color_Width
            string strTBD1 = null;            //Not clear, usually "-1"
            string point1 = null;           //start poinit of wire
            string point2 = null;           //stop point of wire
            string strNet = null;           //Net string
            int NetCount = 0;               //Use to assign numbers as net to every point
            int tempIndex1 = 0;             //net of point1
            int tempIndex2 = 0;             //net of point2

            //Point - Net: Only net Found in Wire net and Pin
            Dictionary<string, string> KeyPointNet = new Dictionary<string, string>();
            //Point - Net: Net are numbers, sort by wire
            Dictionary<string, int> PointNetIndex = new Dictionary<string, int>();
            //Net - Points: Points with the same number Net
            Dictionary<int, List<string>> NetPoints = new Dictionary<int, List<string>>();
            //Point - Net: All
            Dictionary<string, string> PointNet = new Dictionary<string, string>();
            //Point - ColorWidth: Defined by wire
            //待优化
            List<string[]> PointColorWidth = new List<string[]>();

            //Create regulartion
            Regex rxWire = new Regex(strPattern_Wire, RegexOptions.Compiled);
            Regex rxWireNet = new Regex(strPattern_Net, RegexOptions.Compiled);
            Regex rxPinNet = new Regex(strPattern_PinNet, RegexOptions.Compiled);
            Match myMatch;

            string strPageFile = "page" + s + ".csa";

            try
            {
                // Open WarningLog to record errors


                //Check if the file exists
                FileInfo pagecsa = new FileInfo(strPageFile);
                if (!pagecsa.Exists)
                {
                    StreamWriter warningLog = new StreamWriter("Warning.log", true);
                    warningLog.WriteLine(strPageFile + " dose not exsit!");
                    warningLog.Close();
                    return;
                }

                // The using statement also closes the StreamReader.
                using (StreamReader sr = new StreamReader(strPageFile))
                {
                    string rdLine;
                    // Read and Analyze lines from the file until the end of the file is reached.
                    while (sr.Peek() > -1)
                    {
                        rdLine = sr.ReadLine();
                        // Check the Pin Net
                        myMatch = rxPinNet.Match(rdLine);
                        if (myMatch.Success)
                        {
                            GroupCollection groups = myMatch.Groups;
                            point1 = groups[1].ToString();
                            strNet = UpdateNet(groups[2].ToString());
                            KeyPointNet.Add(point1, strNet);
                        }
                        else
                        {
                            // Check the Net next to Wire
                            if (blnWireFound)
                            {
                                myMatch = rxWireNet.Match(rdLine);
                                blnWireFound = false;
                                if (myMatch.Success)
                                {
                                    GroupCollection groups = myMatch.Groups;
                                    strNet = UpdateNet(groups[1].ToString());
                                    KeyPointNet.Add(point1, strNet);
                                    continue;
                                }
                            }

                            // Check the Wire
                            myMatch = rxWire.Match(rdLine);
                            if (myMatch.Success)
                            {
                                blnWireFound = true;
                                GroupCollection groups = myMatch.Groups;
                                strColorWidth = groups[1].ToString();
                                strTBD1 = groups[2].ToString();
                                point1 = groups[3].ToString();
                                point2 = groups[4].ToString();

                                // Point Color: Point at the junction may have different color
                                PointColorWidth.Add(new string[] { point1, strColorWidth });
                                PointColorWidth.Add(new string[] { point2, strColorWidth });
                                if ((PointNetIndex.ContainsKey(point1) == false) && (PointNetIndex.ContainsKey(point2) == false))
                                {
                                    // Neither of the two point apeared before
                                    // use number to temperarly mark the point
                                    PointNetIndex.Add(point1, NetCount);
                                    PointNetIndex.Add(point2, NetCount);
                                    NetPoints.Add(NetCount, new List<string> { point1, point2 });
                                    NetCount++;
                                }
                                else
                                {
                                    // One of the two point appeared berore, assign net to the other point 
                                    if ((PointNetIndex.ContainsKey(point1) == true) && (PointNetIndex.ContainsKey(point2) == false))
                                    {
                                        tempIndex1 = PointNetIndex[point1];
                                        PointNetIndex.Add(point2, tempIndex1);
                                        NetPoints[tempIndex1].Add(point2);
                                    }
                                    else if ((PointNetIndex.ContainsKey(point1) == false) && (PointNetIndex.ContainsKey(point2) == true))
                                    {
                                        tempIndex2 = PointNetIndex[point2];
                                        PointNetIndex.Add(point1, tempIndex2);
                                        NetPoints[tempIndex2].Add(point1);
                                    }
                                    else
                                    {
                                        // Both of the two points apeared, assign the smaller net to bigger one and remove the bigger one
                                        tempIndex1 = PointNetIndex[point1];
                                        tempIndex2 = PointNetIndex[point2];
                                        if (tempIndex1 > tempIndex2)
                                        {
                                            foreach (string strPoint in NetPoints[tempIndex1])
                                            {
                                                PointNetIndex[strPoint] = tempIndex2;
                                            }
                                            NetPoints[tempIndex2].AddRange(NetPoints[tempIndex1]);
                                            NetPoints.Remove(tempIndex1);
                                        }
                                        else if (tempIndex1 < tempIndex2)
                                        {
                                            foreach (string strPoint in NetPoints[tempIndex2])
                                            {
                                                PointNetIndex[strPoint] = tempIndex1;
                                            }
                                            NetPoints[tempIndex1].AddRange(NetPoints[tempIndex2]);
                                            NetPoints.Remove(tempIndex2);
                                        }
                                    }
                                }
                            }
                            else
                                blnWireFound = false;
                        }
                    }
                }

                // Combine KeyPoint-Net and Point-NetIndex to Point-Net
                foreach (KeyValuePair<string, string> kvp in KeyPointNet)
                {
                    if(PointNetIndex.ContainsKey(kvp.Key))
                    {
                        tempIndex1 = PointNetIndex[kvp.Key];
                        if(NetPoints.ContainsKey(tempIndex1))
                        {
                            foreach (string strPoint in NetPoints[tempIndex1])
                            {
                                PointNet.Add(strPoint, kvp.Value);
                            }
                        }
                        else
                        {
                            StreamWriter warningLog = new StreamWriter("Warning.log", true);
                            warningLog.WriteLine("Error:\tPage" + s + ":\t" + kvp.Key + "\thas a wrong net Index!");
                            warningLog.Close();
                        }
                    }
                    else
                    {
                        StreamWriter warningLog = new StreamWriter("Warning.log", true);
                        warningLog.WriteLine("Error:\tPage" + s + ":\t" + kvp.Key + "\thas a net without a line!");
                        warningLog.Close();
                    }

                }

                // Combine Point-ColorWith and Point-Net to Net-ColorWidth
                foreach (string[] pcw in PointColorWidth)
                {

                    //Check if the Point is on example color line
                    if (PointNet.ContainsKey(pcw[0]) == true)
                    {
                        //Check if the color is in Color Table
                        if (dctNumber_ColorWidth.ContainsKey(pcw[1]) == false)
                        {
                            // record the number that has no corresponding color_thick 
                            StreamWriter warningLog = new StreamWriter("Warning.log", true);
                            warningLog.WriteLine("Warning:\tPage " + s + "\tPoint:\t" + pcw[1] + "\tcould not find the corresponding color/thick");
                            warningLog.Close();
                            string strNewColor = "UndefinedColor" + dctNumber_ColorWidth.Count().ToString();
                            dctNumber_ColorWidth.Add(pcw[1], strNewColor);
                            lstCWPri.Add(strNewColor);

                        }


                        string keyNet = PointNet[pcw[0]];
                        string keyCW = dctNumber_ColorWidth[pcw[1]];

                        // the duplicate value will not add in
                        if (dctNet_ColorWidth.ContainsKey(keyNet))
                        {
                            // there is an old color_width in dictionary
                            string keyCW2 = dctNet_ColorWidth[keyNet];
                            // compare the two color_width of the to net, only use the different one
                            if (keyCW.Equals(keyCW2) == false)
                            {
                                //new net will be record to the multi-colorwidht-net list
                                if (lstMultiCWnet.Contains(keyNet) == false)
                                    lstMultiCWnet.Add(keyNet);

                                int intPri1 = lstCWPri.IndexOf(keyCW);
                                int intPri2 = lstCWPri.IndexOf(keyCW2);


                                // only changed when the new one of of higher priority
                                if (intPri1 < intPri2)
                                    dctNet_ColorWidth[keyNet] = keyCW;

                                // check if it contain in duplicated dct
                                if (dctDumpNetColorWidth.ContainsKey(keyNet) == true)
                                {
                                    // the old CW is lower priority, move it to the duplication dct
                                    if (intPri1 < intPri2)
                                        dctDumpNetColorWidth[keyNet].Add(keyCW2);
                                    else
                                    {
                                        // Check if the new CW is in the duplicaiotn dct
                                        if (dctDumpNetColorWidth[keyNet].Contains(keyCW) == false)
                                            dctDumpNetColorWidth[keyNet].Add(keyCW);
                                    }
                                }
                                else
                                {
                                    //the net is new, add into the dictionry, the value should be the higher priorty CW
                                    if (intPri1 < intPri2)
                                        dctDumpNetColorWidth.Add(keyNet, new List<string> { keyCW2 });
                                    else
                                        dctDumpNetColorWidth.Add(keyNet, new List<string> { keyCW });
                                }
                            }
                            //same CW will be ignored
                        }
                        else
                        {
                            // new Net- Color_Width pair
                            dctNet_ColorWidth.Add(keyNet, keyCW);
                        }
                    }
                    else
                    {
                        // record the Point that has no net
                        StreamWriter warningLog = new StreamWriter("Warning.log", true);
                        warningLog.WriteLine("Warning:\tPage " + s + "\tPoint (\t" + pcw[0] + "\t) has no net assigned");
                        warningLog.Close();
                    }
                }
            }

            catch (Exception e)
            {
                // Let the user know what go wrong.
                Console.WriteLine("Error in found in page" + s + ".csa.");
                Console.WriteLine(e.Message);
                StreamWriter warningLog = new StreamWriter("Warning.log", true);
                warningLog.WriteLine("Error:\tPage" + s + ":\tcatch an error:\t" + e.Message);
                warningLog.Close();
            }
        }

        //Read dialcnet.dat
        private static void ReadDialcnet()
        {
            

            FileInfo dialcnetdat = new FileInfo("dialcnet.dat");
            if (!dialcnetdat.Exists)
            {
                // if the input.txt file doesn't exist, give a warning.
                StreamWriter warningLog = new StreamWriter("Warning.log", true);
                warningLog.WriteLine("No dialcnet.dat file found!");
                warningLog.Close();
            }
            else
            {
                StreamReader srDialcnet = new StreamReader("dialcnet.dat");

                //dump the first line
                srDialcnet.ReadLine();

                string strDialcNet = null;

                string rdLine;

                // some nets only appear in dialc.dat only, add a color width mark "DialcNet" with the lowest priority for them
                dctNumber_ColorWidth.Add("0", "DialcNet");
                lstCWPri.Add("DialcNet");

                while (srDialcnet.Peek() > -1)
                {
                    rdLine = srDialcnet.ReadLine().Trim().ToUpper();

                    //dump the last line
                    if (srDialcnet.Peek() > -1)
                    {
                        // Ignore the blank line
                        if (rdLine.Length >= 1)
                        {
                            strDialcNet = rdLine.Split(' ')[0];
                            if (lstDialcNet.Contains(strDialcNet))
                            {
                                continue;
                            }
                            else
                            {
                                lstDialcNet.Add(strDialcNet);
                                if (dctNet_ColorWidth.ContainsKey(strDialcNet) == false)
                                    dctNet_ColorWidth.Add(strDialcNet, "DialcNet");
                            }
                        }
                    }
                }

                srDialcnet.Close();
            }
        }


        private static void ReadVerilog()
        {
            string rdLine;                      // use to read line for input.txt & verilog.v
            string strNet1;                     // net 1 in tie net
            string strNet2;                     // net 2 in tie net
            string strNetBasic;                 // Basic Net
            string strNetShort;                 // Short Net
            int intCWPri;                       // Net CW Pri, index of the list, the smaller the higher
            string strPriNet;                   // Net with a highest Priority in a short net link
            string strPriCW;                    // a highest Priority CW
            int intCW_Short;
            string strShortPriCW;

            Boolean blnTag_Net = false;
            Boolean blnTag_Aliases = false;

            string strXML_NetNumber;
            string strXML_NetName;
            string strNetNumber1;                     // netNumber 1 in tie net
            string strNetNumber2;                     // netNumber 2 in tie net

            List<string> lstTempLink = new List<string>();

            Dictionary<string, List<string>> dctTempShortNet = new Dictionary<string, List<string>>();

            FileInfo verilog = new FileInfo("verilog.xcon");
            if (!verilog.Exists)
            {
                //blnVerilog = false;
                StreamWriter warningLog = new StreamWriter("Warning.log", true);
                warningLog.WriteLine("Verilog.xcon is not found!");
                warningLog.Close();
            }
            else
            {
                //blnVerilog = true;
                // some nets only appear in verilog file only, add a color width mark "VerilogNet" with the lowest priority for them
                dctNumber_ColorWidth.Add("-1", "VerilogNet");
                lstCWPri.Add("VerilogNet");

                StreamReader srVerilog = new StreamReader("verilog.xcon");
                //Regex rxTie = new Regex(strPattern_Tie, RegexOptions.Compiled);
                Regex rxXML_NetStart = new Regex(strPattern_XML_NetStart, RegexOptions.Compiled);
                Regex rxXML_NetNumber = new Regex(strPattern_XML_NetNumber, RegexOptions.Compiled);
                Regex rxXML_NetName = new Regex(strPattern_XML_NetName, RegexOptions.Compiled);
                Regex rxXML_NetEnd = new Regex(strPattern_XML_NetEnd, RegexOptions.Compiled);
                Regex rxXML_AliasesStart = new Regex(strPattern_XML_AliasesStart, RegexOptions.Compiled);
                Regex rxXML_AliasesNet = new Regex(strPattern_XML_AliasesNet, RegexOptions.Compiled);
                Regex rxXML_AliasesEnd = new Regex(strPattern_XML_AliasesEnd, RegexOptions.Compiled);
                Match myMatch;
                while (srVerilog.Peek() > -1)
                {
                    rdLine = srVerilog.ReadLine();

                    //In 16.6, .con file is a XML file

                    // find net start
                    myMatch = rxXML_NetStart.Match(rdLine);
                    if (myMatch.Success)
                    {
                        if (blnTag_Net)
                        {
                            StreamWriter warningLog = new StreamWriter("Warning.log", true);
                            warningLog.WriteLine("Net TAG start without an END");
                            warningLog.Close();
                        }

                        blnTag_Net = true;
                        rdLine = srVerilog.ReadLine();
                        // find net number
                        myMatch = rxXML_NetNumber.Match(rdLine);
                        if (myMatch.Success)
                        {
                            GroupCollection grpNetNumber = myMatch.Groups;
                            strXML_NetNumber = grpNetNumber[1].ToString().ToUpper();
                          
                            rdLine = srVerilog.ReadLine();
                            // find net name
                            myMatch = rxXML_NetName.Match(rdLine);
                            if (myMatch.Success)
                            {
                                GroupCollection grpNetName = myMatch.Groups;
                                strXML_NetName = grpNetName[1].ToString().ToUpper();

                                if (dctNet_ColorWidth.ContainsKey(strXML_NetName) == false)
                                    dctNet_ColorWidth.Add(strXML_NetName, "VerilogNet");

                                if (dctNetNumber_Net.ContainsKey(strXML_NetNumber))
                                {
                                    StreamWriter warningLog = new StreamWriter("Warning.log", true);
                                    warningLog.WriteLine(strXML_NetNumber+" duplicated in the xcon file");
                                    warningLog.Close();
                                }
                                else
                                {
                                    dctNetNumber_Net.Add(strXML_NetNumber, strXML_NetName);
                                }                                
                            }
                            else
                            {
                                StreamWriter warningLog = new StreamWriter("Warning.log", true);
                                warningLog.WriteLine("NetName does not follow behind NetNumber in Line" + rdLine);
                                warningLog.Close();
                            }
                        }
                        else
                        {
                            StreamWriter warningLog = new StreamWriter("Warning.log", true);
                            warningLog.WriteLine("NetNumber does not follow behind NetStart in Line" + rdLine);
                            warningLog.Close();
                        }

                    }
                    
                    // find net end
                    myMatch = rxXML_NetEnd.Match(rdLine);
                    if (myMatch.Success)
                    {
                        if(blnTag_Net == false)
                        {
                            StreamWriter warningLog = new StreamWriter("Warning.log", true);
                            warningLog.WriteLine("Net TAG End without a start");
                            warningLog.Close();
                        }                         

                        blnTag_Net = false;
                    }

                    // find Aliases start
                    myMatch = rxXML_AliasesStart.Match(rdLine);
                    if (myMatch.Success)
                    {
                        if (blnTag_Aliases)
                        {
                            StreamWriter warningLog = new StreamWriter("Warning.log", true);
                            warningLog.WriteLine("Aliases TAG start without an end");
                            warningLog.Close();
                        }

                        blnTag_Aliases = true;
                    }

                    // find Aliases Net
                    myMatch = rxXML_AliasesNet.Match(rdLine);
                    if (myMatch.Success)
                    {
                        if (blnTag_Aliases)
                        {
                            GroupCollection grpAliases = myMatch.Groups;
                            strNetNumber1 = grpAliases[1].ToString().ToUpper();
                            strNetNumber2 = grpAliases[2].ToString().ToUpper();

                            if (dctNetNumber_Net.ContainsKey(strNetNumber1))
                                strNet1 = dctNetNumber_Net[strNetNumber1];
                            else
                            {
                                StreamWriter warningLog = new StreamWriter("Warning.log", true);
                                warningLog.WriteLine(strNetNumber1 + "Not find the NetName in xcon");
                                warningLog.Close();
                                continue;
                            }
                            if (dctNetNumber_Net.ContainsKey(strNetNumber2))
                                strNet2 = dctNetNumber_Net[strNetNumber2];
                            else
                            {
                                StreamWriter warningLog = new StreamWriter("Warning.log", true);
                                warningLog.WriteLine(strNetNumber2 + "Not find the NetName in xcon");
                                warningLog.Close();
                                continue;
                            }

                            // Only deal with the nets that are different
                            if (strNet1.Equals(strNet2) == false)
                            {

                                // both the net are fake net
                                if ((lstDialcNet.Contains(strNet1) == false) && (lstDialcNet.Contains(strNet2) == false))
                                {
                                    //both two net are checked, verify their base net
                                    if ((lstShortNet.Contains(strNet1) == true) && (lstShortNet.Contains(strNet2) == true))
                                    {

                                        if (dctShortNet_BasicNet[strNet1].Equals(dctShortNet_BasicNet[strNet2]) == false)
                                        {
                                            StreamWriter warningLog = new StreamWriter("Warning.log", true);
                                            warningLog.WriteLine("Error:\tverlig.v:\t" + dctShortNet_BasicNet[strNet1] + "\t" + dctShortNet_BasicNet[strNet2] + "\ttwo basic net are shorted in verilog by two fake net:\t" + strNet1 + "\t" + strNet2);
                                            warningLog.Close();
                                        }
                                    }
                                    //one is checked, the other is not
                                    else if ((lstShortNet.Contains(strNet1) == true) && (lstShortNet.Contains(strNet2) == false))
                                    {
                                        strNetBasic = dctShortNet_BasicNet[strNet1];

                                        if (dctTempShortNet.ContainsKey(strNet2))
                                        {
                                            foreach (string strLinkNet in dctTempShortNet[strNet2])
                                            {
                                                lstTempLink.Add(strLinkNet);
                                            }
                                            foreach (string strShortLinkNet in lstTempLink)
                                            {
                                                dctShortNet_BasicNet.Add(strShortLinkNet, strNetBasic);
                                                lstShortNet.Add(strShortLinkNet);
                                                dctTempShortNet.Remove(strShortLinkNet);
                                            }
                                            lstTempLink.Clear();
                                        }
                                        else
                                        {
                                            dctShortNet_BasicNet.Add(strNet2, strNetBasic);
                                            lstShortNet.Add(strNet2);
                                        }
                                    }
                                    //one is checked, the other is not
                                    else if ((lstShortNet.Contains(strNet1) == false) && (lstShortNet.Contains(strNet2) == true))
                                    {
                                        strNetBasic = dctShortNet_BasicNet[strNet2];

                                        if (dctTempShortNet.ContainsKey(strNet1))
                                        {
                                            foreach (string strLinkNet in dctTempShortNet[strNet1])
                                            {
                                                lstTempLink.Add(strLinkNet);
                                            }
                                            foreach (string strShortLinkNet in lstTempLink)
                                            {
                                                dctShortNet_BasicNet.Add(strShortLinkNet, strNetBasic);
                                                lstShortNet.Add(strShortLinkNet);
                                                dctTempShortNet.Remove(strShortLinkNet);
                                            }
                                            lstTempLink.Clear();
                                        }
                                        else
                                        {
                                            dctShortNet_BasicNet.Add(strNet1, strNetBasic);
                                            lstShortNet.Add(strNet1);
                                        }
                                    }
                                    //both are not checked
                                    else
                                    {
                                        //case 1: both are new
                                        if ((dctTempShortNet.ContainsKey(strNet1) == false) && (dctTempShortNet.ContainsKey(strNet2) == false))
                                        {
                                            dctTempShortNet.Add(strNet1, new List<string>());
                                            dctTempShortNet[strNet1].Add(strNet1);
                                            dctTempShortNet[strNet1].Add(strNet2);
                                            dctTempShortNet.Add(strNet2, dctTempShortNet[strNet1]);
                                        }
                                        //case 2: strNet1 is new, strNet2 has link
                                        else if ((dctTempShortNet.ContainsKey(strNet1) == false) && (dctTempShortNet.ContainsKey(strNet2) == true))
                                        {
                                            dctTempShortNet[strNet2].Add(strNet1);
                                            dctTempShortNet.Add(strNet1, dctTempShortNet[strNet2]);
                                        }
                                        //case 3: strNet1 has link, strNet2 is new
                                        else if ((dctTempShortNet.ContainsKey(strNet1) == true) && (dctTempShortNet.ContainsKey(strNet2) == false))
                                        {
                                            dctTempShortNet[strNet1].Add(strNet2);
                                            dctTempShortNet.Add(strNet2, dctTempShortNet[strNet1]);
                                        }
                                        //case 4: both have link
                                        else
                                        {
                                            //ADD a check for A = B and B = A connection
                                            foreach (string strLinkNet in dctTempShortNet[strNet2])
                                            {
                                                lstTempLink.Add(strLinkNet);
                                                if (dctTempShortNet[strNet1].Contains(strLinkNet) == false)
                                                {
                                                    dctTempShortNet[strNet1].Add(strLinkNet);
                                                }
                                            }
                                            foreach (string strLinkNet in lstTempLink)
                                            {
                                                dctTempShortNet[strLinkNet] = dctTempShortNet[strNet1];
                                            }
                                            lstTempLink.Clear();
                                        }
                                    }
                                }
                                else
                                {
                                    // two dialcenet are shorted, the dialcnet.dat and verilog.v file are conflict, error
                                    if ((lstDialcNet.Contains(strNet1) == true) && (lstDialcNet.Contains(strNet2) == true))
                                    {

                                        StreamWriter warningLog = new StreamWriter("Warning.log", true);
                                        warningLog.WriteLine("Error:\tverilog.v:\t" + strNet1 + "\t" + strNet2 + "\ttwo baisc net are shorted in verilog directly");
                                        warningLog.Close();
                                    }
                                    // one is short net and the other is basic net,normal.
                                    else
                                    {
                                        if (lstDialcNet.Contains(strNet1))
                                        {
                                            strNetBasic = strNet1;
                                            strNetShort = strNet2;
                                        }
                                        else
                                        {
                                            strNetBasic = strNet2;
                                            strNetShort = strNet1;
                                        }

                                        if (dctShortNet_BasicNet.ContainsKey(strNetShort))
                                        {
                                            //shortNet has been dealed, check if it is connected to the same basic net
                                            if (strNetBasic == dctShortNet_BasicNet[strNetShort])
                                            {
                                                // already checked in

                                                continue;
                                            }
                                            else
                                            {
                                                // two basic net short via a fake net, error
                                                StreamWriter warningLog = new StreamWriter("Warning.log", true);
                                                warningLog.WriteLine("Error:\tverilog.v:\t" + strNetBasic + "\t" + dctShortNet_BasicNet[strNetShort] + "\ttwo basic net are shorted in verilog via a fake net:\t" + strNetShort);
                                                warningLog.Close();
                                            }
                                        }
                                        else
                                        {
                                            // short net is new, check if it connected to a list of short net                             
                                            if (dctTempShortNet.ContainsKey(strNetShort))
                                            {
                                                foreach (string strLinkNet in dctTempShortNet[strNetShort])
                                                {
                                                    lstTempLink.Add(strLinkNet);
                                                }
                                                foreach (string strShortLinkNet in lstTempLink)
                                                {
                                                    dctShortNet_BasicNet.Add(strShortLinkNet, strNetBasic);
                                                    lstShortNet.Add(strShortLinkNet);
                                                    dctTempShortNet.Remove(strShortLinkNet);
                                                }
                                                lstTempLink.Clear();
                                            }
                                            else
                                            {
                                                dctShortNet_BasicNet.Add(strNetShort, strNetBasic);
                                                lstShortNet.Add(strNetShort);
                                            }
                                        }
                                    }

                                }
                            }
                            // the same net will be ignore
                        }
                        else
                        {
                            StreamWriter warningLog = new StreamWriter("Warning.log", true);
                            warningLog.WriteLine("Aliases TAG present without a start");
                            warningLog.Close();
                        }
                    }

                    // find Aliases End
                    myMatch = rxXML_AliasesEnd.Match(rdLine);
                    if (myMatch.Success)
                    {
                        if (blnTag_Aliases == false)
                        {
                            StreamWriter warningLog = new StreamWriter("Warning.log", true);
                            warningLog.WriteLine("Aliases TAG End without a start");
                            warningLog.Close();
                        }

                        blnTag_Aliases = false;
                    }

                }// finish reading

                srVerilog.Close();
            }

            // Sort the first priority CW for each short 
            // for the true short net, only mark the basic net
            foreach (KeyValuePair<string, string> kvp in dctShortNet_BasicNet)
            {
                //only record the first priority CW of the basic net
                if (dctNet_ShortColorWidth.ContainsKey(kvp.Value))
                {
                    //only short is new, compare the new short net with the basic
                    strPriCW = dctNet_ShortColorWidth[kvp.Value];
                    intCWPri = lstCWPri.IndexOf(strPriCW);
                    strShortPriCW = dctNet_ColorWidth[kvp.Key];
                    intCW_Short = lstCWPri.IndexOf(strShortPriCW);
                    if (intCW_Short < intCWPri)
                        dctNet_ShortColorWidth[kvp.Value] = strShortPriCW;
                }
                else
                {
                    //both are new, compare the short and basic
                    strPriCW = dctNet_ColorWidth[kvp.Value];
                    intCWPri = lstCWPri.IndexOf(strPriCW);
                    strShortPriCW = dctNet_ColorWidth[kvp.Key];
                    intCW_Short = lstCWPri.IndexOf(strShortPriCW);
                    if (intCW_Short < intCWPri)
                        dctNet_ShortColorWidth.Add(kvp.Value, strShortPriCW);
                    else
                        dctNet_ShortColorWidth.Add(kvp.Value, strPriCW);
                }
            }


            //To Check!!!! the single pin net that not short with others may not appear
            //check the dctTempShortNet for single pin net
            //they are not found in dialcnet.dat, even only one pin
            //HPC for single pin net, the so-called "basic net" is the net has first priority
            if (dctTempShortNet.Count() > 0)
            {
                // single Pin Net is not found in dialcnet but may record in verilog
                foreach (string strSinglePinNet in dctTempShortNet.Keys)
                {
                    if (dctFakeShortNet.ContainsKey(strSinglePinNet) == false)
                    {
                        intCWPri = lstCWPri.IndexOf(dctNet_ColorWidth[strSinglePinNet]);
                        strPriNet = strSinglePinNet;

                        foreach (string strFakeShortNet in dctTempShortNet[strSinglePinNet])
                        {
                            if (lstCWPri.IndexOf(dctNet_ColorWidth[strFakeShortNet]) < intCWPri)
                            {
                                intCWPri = lstCWPri.IndexOf(dctNet_ColorWidth[strFakeShortNet]);
                                strPriNet = strFakeShortNet;
                            }
                        }

                        strPriCW = dctNet_ColorWidth[strPriNet];

                        foreach (string strFakeShortNet in dctTempShortNet[strSinglePinNet])
                        {
                            dctFakeShortNet.Add(strFakeShortNet, strPriNet);
                            lstSinglePinNet.Add(strFakeShortNet);
                            dctNet_ShortColorWidth.Add(strFakeShortNet, strPriCW);
                        }
                    }
                }
            }

            //loop the whole net list to check signle pin net that not short with others
            foreach (KeyValuePair<string, string> kvp in dctNet_ColorWidth)
            {
                if ((lstDialcNet.Contains(kvp.Key) == false) && (lstShortNet.Contains(kvp.Key) == false) && (lstSinglePinNet.Contains(kvp.Key) == false))
                {
                    lstSinglePinNet.Add(kvp.Key);
                }
            }
        }
       

        //Generate the report, using tab to seperate the string
        private static void GenerateReport()
        {
            string strNet = null;
            string strOriginCW = null;
            string strSinglePin = null;
            string strFake = null;
            string strTrueNet = null;
            string strTrueNetCW = null;
            string strSameCW = null;
            string strMainCW = null;
            string strSameWithMain = null;
            string strDup = null;
            string strMultiCW = null;
            

            StreamWriter sw = new StreamWriter("Net_ColorWidth_Analyzer.txt", false);
            sw.WriteLine("Net\tOriginColor_Width\tSinglePinNet?\tFakeNet?\tTrueNet\tTrueNet_ColorWidth\tSameColorWidth?\tHighestPriorityColorWidth\tSameWithHPC?\tDuplicated?\tMulti_ColorWidth");
            // Net  OriginColor_Width   TiedColor_Width Duplicated

            // output the normall net
            foreach (KeyValuePair<string, string> kvp in dctNet_ColorWidth)
            {
                strNet = kvp.Key;
                strOriginCW = kvp.Value;

                //check Single Pin Net


                //check short net
                if (lstShortNet.Contains(strNet))
                {
                    strFake = "True";
                    strTrueNet = dctShortNet_BasicNet[strNet];
                    strTrueNetCW = dctNet_ColorWidth[strTrueNet];
                    strMainCW = dctNet_ShortColorWidth[strTrueNet];
                }
                else
                {
                    // will be cover if found in single pin net
                    strFake = "False";
                    strTrueNet = strNet;
                    strTrueNetCW = strOriginCW;
                    strMainCW = strOriginCW;
                }

                if (lstSinglePinNet.Contains(strNet))
                {
                    strSinglePin = "True";
                    if(dctFakeShortNet.ContainsKey(strNet))
                    {
                        strFake = "True";
                        strTrueNet = dctFakeShortNet[strNet];
                        strTrueNetCW = dctNet_ColorWidth[strTrueNet];
                        strMainCW = dctNet_ShortColorWidth[strTrueNet];
                    }
                    else
                    {
                        strFake = "False";
                        strTrueNet = strNet;
                        strTrueNetCW = strOriginCW;
                        strMainCW = strOriginCW;
                    }
                }
                else
                {
                    strSinglePin = "False";
                }
                    

                //check shortNetColor
                if (strOriginCW.Equals(strTrueNetCW) == true)
                    strSameCW = "True";
                else
                    strSameCW = "False";

                if (strOriginCW.Equals(strMainCW) == true)
                    strSameWithMain = "True";
                else
                    strSameWithMain = "False";




                //check duplicated color in sch
                if (dctDumpNetColorWidth.ContainsKey(strNet))
                {
                    strDup = "True";
                    foreach (string multiCW in dctDumpNetColorWidth[strNet])
                    {
                        strMultiCW = multiCW;
                        sw.WriteLine(strNet + "\t" + strOriginCW + "\t" + strSinglePin + "\t" + strFake + "\t" + strTrueNet + "\t" + strTrueNetCW + "\t" + strSameCW + "\t" + strMainCW + "\t" +strSameWithMain + "\t" + strDup + "\t" + strMultiCW);
                    }
                    
                }
                else
                {
                    strDup = "False";
                    strMultiCW = null;
                    sw.WriteLine(strNet + "\t" + strOriginCW + "\t" + strSinglePin + "\t" + strFake + "\t" + strTrueNet + "\t" + strTrueNetCW + "\t" + strSameCW + "\t" + strMainCW + "\t" + strSameWithMain + "\t" + strDup + "\t" + strMultiCW);
                }

                strNet = null;
                strOriginCW = null;
                strSinglePin = null;
                strFake = null;
                strTrueNet = null;
                strTrueNetCW = null;
                strSameCW = null;
                strMainCW = null;
                strSameWithMain = null;
                strDup = null;
                strMultiCW = null;

            }
            sw.Close();
        }
        
        //Initial the Number => CW Table
        private static void InitialCWTable()
        {
            // Number=>Color_Thick
            dctNumber_ColorWidth.Add("2", "RED");
            dctNumber_ColorWidth.Add("3", "RED_THICK");
            dctNumber_ColorWidth.Add("4", "GREEN");
            dctNumber_ColorWidth.Add("5", "GREEN_THICK");
            dctNumber_ColorWidth.Add("8", "BLUE");
            dctNumber_ColorWidth.Add("9", "BLUE_THICK");
            dctNumber_ColorWidth.Add("16", "YELLOW");
            dctNumber_ColorWidth.Add("17", "YELLOW_THICK");
            dctNumber_ColorWidth.Add("32", "ORANGE");
            dctNumber_ColorWidth.Add("33", "ORANGE_THICK");
            dctNumber_ColorWidth.Add("64", "MONO");
            dctNumber_ColorWidth.Add("65", "MONO_THICK");
            dctNumber_ColorWidth.Add("66", "SALMON");
            dctNumber_ColorWidth.Add("67", "SALMON_THICK");
            dctNumber_ColorWidth.Add("68", "VIOLET");
            dctNumber_ColorWidth.Add("69", "VIOLET_THICK");
            dctNumber_ColorWidth.Add("70", "BROWN");
            dctNumber_ColorWidth.Add("71", "BROWN_THICK");
            dctNumber_ColorWidth.Add("72", "SKYBLUE");
            dctNumber_ColorWidth.Add("73", "SKYBLUE_THICK");
            dctNumber_ColorWidth.Add("74", "WHITE");
            dctNumber_ColorWidth.Add("75", "WHITE_THICK");
            dctNumber_ColorWidth.Add("76", "PEACH");
            dctNumber_ColorWidth.Add("77", "PEACH_THICK");
            dctNumber_ColorWidth.Add("80", "PINK");
            dctNumber_ColorWidth.Add("81", "PINK_THICK");
            dctNumber_ColorWidth.Add("82", "PURPLE");
            dctNumber_ColorWidth.Add("83", "PURPLE_THICK");
            dctNumber_ColorWidth.Add("96", "AQUA");
            dctNumber_ColorWidth.Add("97", "AQUA_THICK");
            dctNumber_ColorWidth.Add("98", "GRAY");
            dctNumber_ColorWidth.Add("99", "GRAY_THICK");
        }

        //Read Input File
        private static void ReadInputCW_Pri()
        {
            FileInfo input = new FileInfo("input.txt");

            StreamWriter warningLog = new StreamWriter("Warning.log", true);

            if (!input.Exists)
            {
                // if the input.txt file doesn't exist, give a warning.
                //blnInput = false;
                warningLog.WriteLine("No input file found!");
                warningLog.Close();
            }
            else
            {
                //blnInput = true;
                StreamReader srblnInput = new StreamReader("Input.txt");
                int CWPri = 0;
                string rdLine;

                while (srblnInput.Peek() > -1)
                {
                    rdLine = srblnInput.ReadLine().Trim().ToUpper();
                    // Ignore the blank line
                    if (rdLine.Length >= 1)
                    {
                        if (lstCWPri.Contains(rdLine) == true)
                        {
                            // record the duplicated Color_Width
                            warningLog.WriteLine("The input contains the same Color_Width: " + rdLine);
                        }
                        else
                        {
                            lstCWPri.Add(rdLine);
                            dctCW_Pri.Add(rdLine, CWPri);
                            CWPri++;
                        }
                    }
                }
                srblnInput.Close();
                warningLog.Close();
            }
        }
        
        //Get Page List from Consle input
        private static void GetPageList(string strPages)
        {

            while (strPages == null)
            {
                Console.WriteLine("No page number found. Please enter page number range to analyze:");
                strPages = Console.ReadLine();
            }

            string[] pages;                     // page numbers
            string[] pageseparators = { "," };    // input page separator: 11,15,17
            string[] continuousPages;           // page use .., eg: 16..27

            int intFirstPage = 0;               // start number of continuous Page
            int intLastPage = 0;                // end number of continuous Page

            //split input to page and continus page
            pages = strPages.Split(pageseparators, StringSplitOptions.RemoveEmptyEntries);
            if (pages.Length > 0)
            {
                pageseparators[0] = "..";
                foreach (string page in pages)
                {
                    if (page != null)
                    {
                        if (page.Contains(".."))
                        {
                            intFirstPage = 0;
                            intLastPage = 0;
                            continuousPages = page.Split(pageseparators, StringSplitOptions.RemoveEmptyEntries);
                            intFirstPage = int.Parse(continuousPages[0]);
                            intLastPage = int.Parse(continuousPages[1]);
                            for (int i = intFirstPage; i <= intLastPage; i++)
                                lstPage.Add(i.ToString());
                        }
                        else
                            lstPage.Add(page);
                    }
                }
            }
        }

        //Update UN to UNNAMED, $ TO _, DELETE \g in global net
        //Add: Delete \BASE 
        private static string UpdateNet(string strNet)
        {
            string strUpdateNet = strNet.Trim();
            int length = strUpdateNet.Length;

            if (strUpdateNet.StartsWith(@"UN$"))
            {
                strUpdateNet = "UNNAMED_" + strUpdateNet.Substring(3);
            }

            if (strUpdateNet.EndsWith(@"\g"))
            {
                strUpdateNet = strUpdateNet.Substring(0, length - 2);
            }

            if (strUpdateNet.EndsWith(@"\BASE"))
            {
                strUpdateNet = strUpdateNet.Substring(0, length - 5);
            }

            strUpdateNet = strUpdateNet.Replace('$', '_');

            return strUpdateNet;
        }

    }
}
