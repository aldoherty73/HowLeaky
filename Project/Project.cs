using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.Schema;
using HowLeaky.SyncModels;
using HowLeaky.DataModels;
using System.Xml.Linq;
using System.Linq;
using HowLeaky.Tools.Helpers;
using HowLeaky.Factories;
using HowLeaky.Tools.XML;
using System.ComponentModel;
using HowLeaky.ModelControllers.Outputs;
using HLRDB;
using System.Data.SQLite;
using System.IO;
using HowLeaky.ModelControllers;
using HowLeaky.OutputModels;
using System.Text;
using HowLeaky.CustomAttributes;
using System.Threading;

namespace HowLeaky
{
    public enum OutputType { CSVOutput, SQLiteOutput, NetCDF }

    public class Project : CustomSyncModel, IXmlSerializable
    {
        public List<Simulation> Simulations { get; set; }
        //Xml Simulation elements for lazy loading the simualtions
        public List<XElement> SimulationElements { get; set; }
        //Base input data models from the parameter files
        public List<InputModel> InputDataModels { get; set; }

        public List<XElement> TypeElements { get; set; }

        public List<OutputDataElement> OutputDataElements { get; set; }

        public DateTime StartRunTime;

        public List<HLBackGroundWorker> BackgroundWorkers;

        public string ContactDetails { get; set; }

        public int CurrentSimIndex = 1;
        public int NoSimsComplete = 0;

        public bool HasOwnExecutableSpace = true;

        public OutputType OutputType = OutputType.SQLiteOutput;
        //public OutputType OutputType = OutputType.NetCDF;

        public delegate void SimCompleteNotifier();

        public SimCompleteNotifier Notifier;

        //Members for the output model
        public SQLiteConnection SQLConn;
        //public HLDBContext DBContext = null;
        //public HLNCFile HLNC = null;
        public string OutputPath { get; set; } = null;
        public string FileName { get; set; }

        public bool WriteMonthlyData = false;
        public bool WriteYearlyData = false;

        public List<XElement> ClimateDatalements;
        public int ClimateDataIndex = -1;
        public bool UseStaggeredMet = true;
        public ClimateInputModel CurrentClimateInputModel;
        bool ThreadLocked = false;

        /// <summary>
        /// Need default constructor for populating via Entity Framework 
        /// </summary>
        public Project()
        {
            Simulations = new List<Simulation>();
            SimulationElements = new List<XElement>();
            InputDataModels = new List<InputModel>();
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileName"></param>
        public Project(string fileName) : this()
        {
            FileName = fileName;
            //Assume this a a legitimate hlk file
            ReadXml(XmlReader.Create(fileName));

            //Check that the data models are OK

            //Run the simulations
            //RunSimulations();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="noProcessors"></param>
        public void Run(int noProcessors = 0)
        {
            //Get the number of processors correct - a negative number will mean thats how many processors to leave to the user
            if (noProcessors < 1)
            {
                noProcessors = Environment.ProcessorCount - noProcessors;
            }
            else
            {
                noProcessors = Math.Max(noProcessors, Environment.ProcessorCount);
            }

            //Load the simulations - lazy load using string paths

            //Just run at the moment - Threading to come later

            //Pop the top simulation from the simulation elements
            foreach (XElement xe in SimulationElements)
            {
                List<InputModel> simModels = SimInputModelFactory.GenerateSimInputModels(xe, InputDataModels);

                Simulation sim = new Simulation(this, simModels);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public XmlSchema GetSchema()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="models"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public List<InputModel> FindInputModels(List<InputModel> models, Type type)
        {
            List<InputModel> foundModels = new List<InputModel>(models.Where(x => x.GetType() == type));
            if (foundModels.Count == 0)
            {
                return null;
            }

            if (type.ToString().ToLower().Contains("pest"))
            {
                return foundModels.DistinctBy(x => x.Name).ToList();
            }
            else
            {
                return foundModels.Take(1).ToList();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="reader"></param>
        public void ReadXml(XmlReader reader)
        {

            XDocument doc = XDocument.Load(reader);

            //Get the list of models
            XElement projectElement = doc.Element("Project");

            //Set the project properties
            CreatedBy = XMLUtilities.readXMLAttribute(projectElement.Attribute("CreatedBy"));
            DateTime? createdDate = DateUtilities.TryParseDate(XMLUtilities.readXMLAttribute(projectElement.Attribute("CreationDate")).Split(new char[] { ' ' })[0], "dd/MM/yyyy");
            CreatedDate = createdDate == null ? new DateTime(1, 1, 1) : createdDate.Value;
            ContactDetails = XMLUtilities.readXMLAttribute(projectElement.Attribute("ContactDetails"));
            ModifiedBy = XMLUtilities.readXMLAttribute(projectElement.Attribute("ModifiedBy"));

            Name = projectElement.Element("Name").Value.ToString();

            //Read all of the climate data models
            ClimateDatalements = new List<XElement>(projectElement.Elements("ClimateData").Elements("DataFile"));

            //Read all of the models
            List<XElement> TemplateElements = new List<XElement>(projectElement.Elements().Where(x => x.Name.ToString().Contains("Templates")));
            //List<XElement> TypeElements = new List<XElement>();
            TypeElements = new List<XElement>();

            foreach (XElement te in TemplateElements)
            {
                foreach (XElement xe in te.Elements())
                {
                    TypeElements.Add(xe);
                }
            }
            //Read all of the simualtions
            SimulationElements = new List<XElement>();

            foreach (XElement simChild in projectElement.Elements("Simulations").Elements())
            {
                if (simChild.Name.ToString() == "SimulationObject")
                {
                    SimulationElements.Add(simChild);
                }
                else if (simChild.Name.ToString() == "Folder")
                {
                    SimulationElements.AddRange(simChild.Elements("SimulationObject"));
                }
            }

            //List<XElement> TestSims = new List<XElement>(SimulationElements.Where(xe => xe.Elements("ptrStation").Attributes("href").FirstOrDefault().Value.ToString().Contains("2300_14925")).ToList());

            //InputDataModels = new List<InputModel>();

            //Create input models from the xml elements
            foreach (XElement xe in TypeElements)
            {
                InputDataModels.Add(RawInputModelFactory.GenerateRawInputModel(Path.GetDirectoryName(FileName).Replace("\\", "/"), xe));
            }

            List<HLController> BaseControllers = new List<HLController>();

            //Initialise the models
            foreach (InputModel im in InputDataModels)
            {
                im.Init();
                if (im.GetType() == typeof(PesticideInputModel))
                {
                    im.Name = im.Name.Split(new char[] { '_' }, StringSplitOptions.RemoveEmptyEntries)[0].ToLower();
                }
            }

            // BaseControllers.Add(new VegetationController(null, new List<InputModel>() { InputDataModels.FirstOrDefault(x => x.GetType().BaseType == (typeof(VegInputModel))) }));
            BaseControllers.Add(new SoilController(null, new List<InputModel>() { InputDataModels.FirstOrDefault(x => x.GetType() == typeof(SoilInputModel)) }));

            //Optional Controllers/Models
            BaseControllers.Add(new VegetationController(null, new List<InputModel>() { InputDataModels.Where(x => x.GetType().BaseType == (typeof(VegInputModel))).FirstOrDefault() }));
            BaseControllers.Add(FindInputModels(InputDataModels, typeof(IrrigationInputModel)) == null ? null : new IrrigationController(null, FindInputModels(InputDataModels, typeof(IrrigationInputModel))));
            BaseControllers.Add(FindInputModels(InputDataModels, typeof(TillageInputModel)) == null ? null : new TillageController(null, FindInputModels(InputDataModels, typeof(TillageInputModel))));
            BaseControllers.Add(FindInputModels(InputDataModels, typeof(PesticideInputModel)) == null ? null : new PesticideController(null, FindInputModels(InputDataModels, typeof(PesticideInputModel))));
            BaseControllers.Add(FindInputModels(InputDataModels, typeof(PhosphorusInputModel)) == null ? null : new PhosphorusController(null, FindInputModels(InputDataModels, typeof(PhosphorusInputModel))));

            BaseControllers.Add(FindInputModels(InputDataModels, typeof(NitrateInputModel)) == null ? null : new NitrateController(null, FindInputModels(InputDataModels, typeof(NitrateInputModel))));
            BaseControllers.Add(FindInputModels(InputDataModels, typeof(DINNitrateInputModel)) == null ? null : new DINNitrateController(null, FindInputModels(InputDataModels, typeof(DINNitrateInputModel))));

            BaseControllers.Add(FindInputModels(InputDataModels, typeof(SolutesInputModel)) == null ? null : new SolutesController(null, FindInputModels(InputDataModels, typeof(SolutesInputModel))));
            BaseControllers.Add(new ClimateController(null));
            //Get a list of outputs
            OutputDataElements = OutputModelController.GetProjectOutputs(BaseControllers, true);

            //Trim the outputs down
            List<HowLeaky.OutputModels.OutputDataElement> currentElements = new List<OutputDataElement>();
            //currentElements.Add(new ClimateController(null).GetOutputModels()[0].OutputDataElements.FirstOrDefault(e => e.Name == "Rain"));
            currentElements.Add(OutputDataElements.FirstOrDefault(e => e.Name == "Rain"));
            currentElements.Add(OutputDataElements.FirstOrDefault(e => e.Name == "CropEvapoTranspiration"));
            currentElements.Add(OutputDataElements.FirstOrDefault(e => e.Name == "DeepDrainage"));
            currentElements.Add(OutputDataElements.FirstOrDefault(e => e.Name == "Runoff"));
            currentElements.Add(OutputDataElements.FirstOrDefault(e => e.Name == "HillSlopeErosion"));
            currentElements.Add(OutputDataElements.FirstOrDefault(e => e.Name == "ParticPExport"));
            currentElements.Add(OutputDataElements.FirstOrDefault(e => e.Name == "PhosExportDissolve"));
            currentElements.AddRange(OutputDataElements.Where(e => e.Name.Contains("PestLostInRunoffWater")));
            currentElements.AddRange(OutputDataElements.Where(e => e.Name.Contains("PestLostInRunoffSediment")));

            //Nitrate
            currentElements.AddRange(OutputDataElements.Where(e => e.Name.Contains("N03NRunoffLoad")));
            currentElements.AddRange(OutputDataElements.Where(e => e.Name.Contains("DINDrainage")));

            OutputDataElements.Clear();

            OutputDataElements.AddRange(currentElements);
            //Create the Climate models - these aren't deserialised so don't come out of the factory
            //foreach (XElement xe in ClimateDatalements)
            //{
            //    ClimateInputModel cim = new ClimateInputModel();
            //    cim.FileName = xe.Attribute("href").Value.ToString().Replace("\\", "/");

            //    if (cim.FileName.Contains("./"))
            //    {
            //        cim.FileName = (Path.GetDirectoryName(FileName).Replace("\\", "/") + "/" + cim.FileName);
            //    }

            //    InputDataModels.Add(cim);
            //}

            //Initialise the models
            //foreach (InputModel im in InputDataModels)
            //{
            //    im.Init();
            //}

            //int count = 0;
            ////Create the simualtions
            //foreach (XElement xe in SimulationElements)
            //{
            //    count++;

            //    //For Testing
            //    //if(xe == SimulationElements[0])

            //    Simulations.Add(SimulationFactory.GenerateSimulationXML(this, xe, InputDataModels));

            //    if (count > 1000) break;
            //}

            //OutputDataElements = OutputModelController.GetProjectOutputs(this);

        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="writer"></param>
        public void WriteXml(XmlWriter writer)
        {
            //This will output the models to a project directory
            throw new NotImplementedException();
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="numberOfThreads"></param>
        public void RunSimulations(int numberOfThreads = -1)
        {
            int noCoresToUse = numberOfThreads;
            int noCores = Environment.ProcessorCount;

            if (noCoresToUse <= 0)
            {
                noCoresToUse += noCores;
            }

            //Set up outputs (on main thread)
            if (OutputType == OutputType.CSVOutput)
            {
                FileInfo hlkFile = new FileInfo(FileName);

                if (OutputPath == null)
                {
                    OutputPath = hlkFile.Directory.FullName.Replace("\\", "/");
                }

                if (!OutputPath.Contains(":") || OutputPath.Contains("./"))
                {
                    DirectoryInfo outDir = new DirectoryInfo(Path.Combine(hlkFile.Directory.FullName, OutputPath));
                    if (!outDir.Exists)
                    {
                        outDir.Create();
                    }

                    OutputPath = outDir.FullName;
                }
            }
            else if (OutputType == OutputType.SQLiteOutput)
            {
                SQLConn = CreateSQLiteConnection(OutputPath);
            }
            else if (OutputType == OutputType.NetCDF)
            {
                //if (HLNC == null)
                //{
                //  //  HLNC = new HLNCFile(this, this.Simulations[0].StartDate, this.Simulations[0].EndDate, FileName.Replace(".hlk", ".nc"));
                //}
            }
            //SQLite

            //Reset the counters
            CurrentSimIndex = 1;
            NoSimsComplete = 0;

            StartRunTime = DateTime.Now;

            //Create a list of background workers
            BackgroundWorkers = new List<HLBackGroundWorker>(noCoresToUse);

            //Populate the Background workers and run
            for (int i = 0; i < noCoresToUse; i++)
            {
                BackgroundWorkers.Add(new HLBackGroundWorker());
                BackgroundWorkers[i].DoWork += HLBackgroundWorker_DoWork;
                BackgroundWorkers[i].RunWorkerCompleted += HLBackgroundWorker_RunWorkerCompleted;

                BackgroundWorkers[i].WorkerReportsProgress = true;
                BackgroundWorkers[i].WorkerSupportsCancellation = true;


                //Simulation sim = GetSimulationElement(null);

                //if (sim != null)
                // {
                // BackgroundWorkers[i].Sim = sim;
                // BackgroundWorkers[i].RunWorkerAsync(new List<object>(new object[] { xe, handler }));
                //BackgroundWorkers[i].RunWorkerAsync(new List<object>(new object[] { sim }));
                BackgroundWorkers[i].RunWorkerAsync();

                //}
            }

            if (HasOwnExecutableSpace)
            {
                while (NoSimsComplete < Simulations.Count)
                {
                    System.Threading.Thread.Sleep(500);
                }
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="OutputPath"></param>
        /// <param name="FileName"></param>
        /// <returns></returns>
        public SQLiteConnection CreateSQLiteConnection(string OutputPath = null, string FileName = null)
        {

            SQLiteConnection Connection = new SQLiteConnection();

            if (OutputPath == null)
            {
                OutputPath = this.FileName.Replace(".hlk", ".sqlite");
            }
            else
            {
                if (FileName == null)
                {
                    OutputPath = Path.Combine(OutputPath, new FileInfo(this.FileName).Name.Replace(".hlk", ".sqlite"));
                }
                else
                {
                    OutputPath = Path.Combine(OutputPath, FileName + ".sqlite");
                }
            }

            OutputPath = OutputPath.Replace("\\", "/");

            //if (SQLConn != null)// && SQLConn.State == System.Data.ConnectionState.Open)
            //{
            //    SQLConn.Close();

            //}

            if (File.Exists(OutputPath))
            {
                File.Delete(OutputPath);
            }

            SQLiteConnection.CreateFile(OutputPath);

            Connection = new SQLiteConnection("Data Source=" + OutputPath + ";Version=3; DSQLITE_THREADSAFE=2");
            Connection.Open();

            //Will need to create tables
            //Data
            string sql = "create table data (SimId int, Day int," + String.Join(" double,", OutputDataElements.Where(j => j.IsSelected == true).Select(x => x.Name)) + " double)";

            SQLiteCommand command = new SQLiteCommand(sql, Connection);
            command.ExecuteNonQuery();

            //Annual sum data
            sql = "create table annualdata (SimId int, Year int," + String.Join(" double,", OutputDataElements.Where(j => j.IsSelected == true).Select(x => x.Name)) + " double)";

            command = new SQLiteCommand(sql, Connection);
            command.ExecuteNonQuery();

            //Annual sum average data
            sql = "create table annualaveragedata (SimId int," + String.Join(" double,", OutputDataElements.Where(j => j.IsSelected == true).Select(x => x.Name)) + " double)";

            command = new SQLiteCommand(sql, Connection);
            command.ExecuteNonQuery();

            //Outputs
            sql = "create table outputs (Name string, Description string, Units string, Controller string)";

            command = new SQLiteCommand(sql, Connection);
            command.ExecuteNonQuery();

            StringBuilder sb = new StringBuilder();

            sb.Append("INSERT INTO OUTPUTS (Name, Description , Units, Controller) VALUES ");

            foreach (OutputDataElement ode in OutputDataElements)
            {
                string comma = ",";

                if (ode == OutputDataElements.First())
                {
                    comma = "";
                }

                sb.Append(comma + "(\"" + ode.Name + "\",\"" + ode.Output.Description + "\",\"" + ode.Output.Unit + "\",\"" + ode.HLController.GetType().Name + "\")");
            }

            sql = sb.ToString();

            command = new SQLiteCommand(sql, Connection);
            command.ExecuteNonQuery();


            //Simulations
            sql = "create table simulations (Id int, Name string, StartDate DATETIME, EndDate DATETIME)";

            command = new SQLiteCommand(sql, Connection);
            command.ExecuteNonQuery();

            //Models
            sql = "create table models (SimId int, Name string, InputType string, LongName string)";

            command = new SQLiteCommand(sql, Connection);
            command.ExecuteNonQuery();

            //command = new SQLiteCommand("PRAGMA foreign_keys = OFF;  PRAGMA journal_mode = OFF; PRAGMA locking_mode = EXCLUSIVE; PRAGMA count_changes = OFF;  PRAGMA auto_vacuum = NONE;", SQLConn);
            command = new SQLiteCommand("PRAGMA foreign_keys = OFF;", Connection);
            command.ExecuteNonQuery();

            return Connection;

        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Simulation GetSimulationElement()
        {
            Simulation result = null;

            if (Simulations.Count > CurrentSimIndex)
            {
                result = Simulations[CurrentSimIndex];
                CurrentSimIndex++;
            }

            if (result != null)
            {
                result.Index = Simulations.IndexOf(result) + 1;
            }
            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="doneSim"></param>
        /// <returns></returns>
        public Simulation GetSimulationElement(Simulation doneSim)
        {
            while (ThreadLocked)
            {
                Thread.SpinWait(50);
            }

            Simulation nextSim = null;

            if (doneSim != null)
            {
                Simulations.Remove(doneSim);
            }

            if (Simulations == null || Simulations.Count == 0)
            {
                ThreadLocked = true;

                //Read the first Met Inputmodel
                if (CurrentClimateInputModel != null)
                {
                    InputDataModels.Remove(CurrentClimateInputModel);
                    CurrentSimIndex = 1;
                }

                if (ClimateDataIndex < ClimateDatalements.Count)
                {
                    CurrentClimateInputModel = new ClimateInputModel();

                    CurrentClimateInputModel.FileName = ClimateDatalements[ClimateDataIndex].Attribute("href").Value.ToString().Replace("\\", "/");

                    if (CurrentClimateInputModel.FileName.Contains("./"))
                    {
                        CurrentClimateInputModel.FileName = (Path.GetDirectoryName(FileName).Replace("\\", "/") + "/" + CurrentClimateInputModel.FileName);
                    }

                    CurrentClimateInputModel.Init();

                    //CurrentSimIndex = 0;

                    InputDataModels.Add(CurrentClimateInputModel);

                    List<XElement> currentMetSims = SimulationElements.Where(xe => xe.Elements("ptrStation").Attributes("href").FirstOrDefault().Value.ToString().Contains(CurrentClimateInputModel.Name)).ToList();

                    foreach (XElement xe in currentMetSims)
                    {
                        Simulations.Add(SimulationFactory.GenerateSimulationXML(this, xe, InputDataModels));
                        Simulations.Last().Index = Simulations.Count + NoSimsComplete;
                    }

                    ClimateDataIndex++;
                }
                ThreadLocked = false;
            }


            if (Simulations.Count > 0)
            {
                nextSim = Simulations.FirstOrDefault(s => s.Index == CurrentSimIndex);
                CurrentSimIndex++;
            }

            return nextSim;
        }

        /// <summary>
        /// 
        /// </summary>
        private void CancelBackGroundWorkers()
        {
            foreach (BackgroundWorker bw in BackgroundWorkers)
                if (bw.IsBusy)
                {
                    bw.CancelAsync();
                }

        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HLBackgroundWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            HLBackGroundWorker hlbw = (HLBackGroundWorker)sender;

            //Get a new met file
            int MyMetIndex = ++ClimateDataIndex;
            ClimateInputModel MyClimateInputModel;
            List<Simulation> MySimulations = new List<Simulation>();
            SQLiteConnection MySQLConn = new SQLiteConnection();


            if (MyMetIndex < ClimateDatalements.Count)
            {
                MyClimateInputModel = new ClimateInputModel();

                MyClimateInputModel.FileName = ClimateDatalements[MyMetIndex].Attribute("href").Value.ToString().Replace("\\", "/");

                if (MyClimateInputModel.FileName.Contains("./"))
                {
                    MyClimateInputModel.FileName = (Path.GetDirectoryName(FileName).Replace("\\", "/") + "/" + MyClimateInputModel.FileName);
                }

                MyClimateInputModel.Init();

                DirectoryInfo OutDir = new DirectoryInfo(Path.GetDirectoryName(FileName)).CreateSubdirectory("Output");

                MySQLConn = CreateSQLiteConnection(OutDir.FullName, MyClimateInputModel.Name);

                InputDataModels.Add(MyClimateInputModel);

                List<XElement> currentMetSims = SimulationElements.Where(xe => xe.Elements("ptrStation").Attributes("href").FirstOrDefault().Value.ToString().Contains(MyClimateInputModel.Name)).ToList();

                foreach (XElement xe in currentMetSims)
                {
                    MySimulations.Add(SimulationFactory.GenerateSimulationXML(this, xe, InputDataModels));
                    MySimulations.Last().Index = MySimulations.Count;
                    //((SQLiteOutputModelController)MySimulations.Last().OutputModelController).SQLConn = MySQLConn;
                }

                foreach (Simulation s in MySimulations)
                {
                    try
                    {
                        s.Run(MySQLConn);
                    }
                    catch (Exception ex)
                    {
                        string message = ex.Message;
                    }
                }

                MySQLConn.Close();

                InputDataModels.Remove(MyClimateInputModel);

                // ClimateDataIndex++;
            }

            // Close



            //List<object> Arguments = e.Argument as List<object>;

            //Simulation sim = (Simulation)Arguments[0];

            ////hlbw.Sim = SimulationFactory.GenerateSimulationXML(simElement, InputDataModels);
            ////hlbw.Sim.Id = SimulationElements.IndexOf(simElement) + 1;

            //hlbw.Sim = sim;
            //hlbw.Sim

            //Setup output controllers
            //try
            //{
            //    hlbw.Sim.Run();
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine(ex.Message);
            //}
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HLBackgroundWorker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {

            }
            else if (e.Error != null)
            {

            }
            else
            {

            }

            if (Notifier != null)
            {
                Notifier();
            }

            HLBackGroundWorker hlbw = (HLBackGroundWorker)sender;

            //hlbw.Sim.OutputModelController.Finalise();

            NoSimsComplete++;

            //Update Progress
            //Console.Write("\r{0} % Done.", ((double)NoSimsComplete / Simulations.Count * 100).ToString("0.00"));
            Console.Write("\r{0} % Done.", ((double)ClimateDataIndex / ClimateDatalements.Count * 100).ToString("0.00"));


            //if (NoSimsComplete > Simulations.Count)
            if (NoSimsComplete > SimulationElements.Count)
            {
                DateTime end = DateTime.Now;

                TimeSpan ts = end - StartRunTime;

                Console.WriteLine(ts);
            }

            // Simulation nextSim = GetSimulationElement(hlbw.Sim);

            //if (nextSim == null)
            //{
            //    return;
            //}
            //else
            //{
            // hlbw.Sim = nextSim;
            //hlbw.RunWorkerAsync(new List<object>(new object[] { nextSim }));
            if (ClimateDataIndex < ClimateDatalements.Count)
            {
                hlbw.RunWorkerAsync();
            }
            //}

            //if (NoSimsComplete == Simulations.Count)
            //if (NoSimsComplete == SimulationElements.Count)
            //{
            //    if (OutputType == OutputType.SQLiteOutput)
            //    {
            //        SQLConn.Close();
            //    }
            //}
        }
    }
}
