﻿using HLRDB;
using HowLeaky.DataModels;
using HowLeaky.OutputModels;
using HowLeaky.Tools.ListExtensions;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HowLeaky.ModelControllers.Outputs
{
    public class SQLiteOutputModelController : OutputModelController
    {
        string InsertString = "";
        //override bool DateIsOutput { get; set; } = false;

        //public HLDBContext DBContext;
        public SQLiteConnection SQLConn { get; set; }

        public SQLiteOutputModelController() : base() { }

        List<List<double>> AnnualSumValues;
        List<double> AnnualAverageValues;
        int currentYear = -1;

        //public HLRDB.Simulation DBSim = null;
        //public List<HLRDB.Data> Data = new List<HLRDB.Data>();

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="Sim"></param>
        //public SQLiteOutputModelController(Simulation Sim, HLDBContext DBContext) : base(Sim)
        //{
        //    //  this.DBContext = DBContext;

        //    PrepareVariableNamesForOutput();
        //}
        /// <summary>
        /// 
        /// </summary>
        /// <param name="Sim"></param>
        /// <param name="SQLConn"></param>
        public SQLiteOutputModelController(Simulation Sim, SQLiteConnection SQLConn) : base(Sim, false)
        {
            //  this.DBContext = DBContext;
            DateIsOutput = false;
            this.SQLConn = SQLConn;
            PrepareVariableNamesForOutput();
        }


        static void PrepareConnection()
        {

        }

        /// <summary>
        /// 
        /// </summary>
        public override void PrepareVariableNamesForOutput()
        {
            base.PrepareVariableNamesForOutput();

            List<string> OutputNames = new List<string>(Outputs.Where(j => j.IsSelected).Select(x => x.Name));

            //   List<string> OutputIndicies = new List<string>();

            //    List<string> ProjectOutputNames = new List<string>(Sim.Project.OutputDataElements.Select(x => x.Name));


            //for (int i = 0; i < OutputNames.Count; i++)
            //{
            //    OutputIndicies.Add("x" + (ProjectOutputNames.IndexOf(OutputNames[i]) + 1).ToString());
            //}

            //Prepare an array for annual outputs
            AnnualSumValues = new List<List<double>>();
            AnnualAverageValues = new List<double>(new List<OutputDataElement>(Outputs.Where(x => x.IsSelected)).Count).Fill(0);

            //Add sim number to the average values
            AnnualAverageValues.Insert(0, Sim.Index);

            //OutputNames.Insert(0, "Day");
            //OutputNames.Insert(0, "SimId");

            InsertString = "INSERT INTO [TABLE] ([INDICIES]," + String.Join(",", OutputNames) + ") VALUES ";

            //    InsertString = "INSERT INTO [TABLE] ([INDICIES]," + String.Join(",", OutputIndicies) + ") VALUES ";
        }
        /// <summary>
        /// 
        /// </summary>
        public override void Finalise()
        {
            //DBContext.SaveChanges();
            //Add Daily data
            StringBuilder iString = new StringBuilder();
            //iString.AppendLine("BEGIN;");

            iString.Append(InsertString.Replace("[TABLE]", "DATA").Replace("[INDICIES]", "SimId,Day"));

            for (int i = 0; i < Values.Count; i++)
            {
                iString.Append((i == 0 ? "" : ",") + "(" + String.Join(",", Values[i].Select(v => Math.Round(v, 5))) + ")");
            }

            iString.Append(";");

            //SQLiteCommand command = new SQLiteCommand(iString.ToString(), SQLConn);
            //command.ExecuteNonQuery();

            Values.Clear();

            //Add annual sum data
            //iString = new StringBuilder();
            iString.Append(InsertString.Replace("[TABLE]", "ANNUALDATA").Replace("[INDICIES]", "SimId,Year"));

            for (int i = 0; i < AnnualSumValues.Count; i++)
            {
                iString.Append((i == 0 ? "" : ",") + "(" + String.Join(",", AnnualSumValues[i].Select(v => Math.Round(v, 5))) + ")");
            }

            iString.Append(";");

            //command = new SQLiteCommand(iString.ToString(), SQLConn);
            //command.ExecuteNonQuery();

            AnnualSumValues.Clear();

            //Add annual average data
            //iString = new StringBuilder();
            iString.Append(InsertString.Replace("[TABLE]", "ANNUALAVERAGEDATA").Replace("[INDICIES]", "SimId"));

            iString.Append("(" + String.Join(",", AnnualAverageValues.Select(v => Math.Round(v, 5))) + ")");

            iString.Append(";");

            //command = new SQLiteCommand(iString.ToString(), SQLConn);
            //command.ExecuteNonQuery();

            AnnualAverageValues.Clear();

            //Add the simulation
            string sql = "INSERT INTO SIMULATIONS (Id, Name, StartDate, EndDate) VALUES (" +
                Sim.Index.ToString() + ",\"" + Sim.Name + "\",\"" + Sim.StartDate.ToLongDateString() +
                "\",\"" + Sim.EndDate.ToLongDateString() + "\")";

            iString.Append(sql);
            iString.Append(";");

            //command = new SQLiteCommand(sql, SQLConn);
            //command.ExecuteNonQuery();

            //Models
            //iString = new StringBuilder();
            iString.Append("INSERT INTO MODELS (SimID, Name, InputType, LongName) VALUES ");
            foreach (InputModel im in Sim.InputModels)
            {
                if (im != null)
                {
                    string comma = ",";

                    if (im == Sim.InputModels.First())
                    {
                        comma = "";
                    }
                    if (im.LongName != null)
                    {
                        string[] nameParts = im.LongName.Split(new char[] { ':' });
                        iString.Append(comma + "(" + Sim.Index.ToString() + ",\"" + im.Name + "\",\"" + nameParts[0] + "\",\"" + nameParts[1] + "\")");
                    }
                }
            }

            iString.Append(";");

            try
            {
                // iString.AppendLine(" END;");
                using (var cmd = new SQLiteCommand(SQLConn))
                {
                    while (SQLConn.State == System.Data.ConnectionState.Executing)
                    {
                        Thread.SpinWait(5);
                    }

                    using (var transaction = SQLConn.BeginTransaction())
                    {
                        //SQLiteCommand command = new SQLiteCommand(iString.ToString(), SQLConn);
                        cmd.CommandText = iString.ToString();
                        cmd.ExecuteNonQuery();

                        transaction.Commit();
                    }
                }
            }
            catch(Exception ex)
            {
                string message = ex.Message;
            }

        }
        /// <summary>
        /// 
        /// </summary>
        public override void WriteDailyData()
        {
            int Day = (Sim.Today - Sim.StartDate).Days + 1;
            int Year = Sim.Today.Year;

            //int DaysInYear = 365 + (DateTime.IsLeapYear(Year) == true ? 1 : 0);

            int noYears = Sim.EndDate.Year - Sim.StartDate.Year + 1;

            List<double> TodaysValues = GetData();

            //Add to annualData
            if (currentYear != Year)
            {
                AnnualSumValues.Add(new List<double>(new List<OutputDataElement>(Outputs.Where(x => x.IsSelected)).Count).Fill(0));
                currentYear = Year;

                //Add SimID and year to the sums
                AnnualSumValues[AnnualSumValues.Count - 1].Insert(0, currentYear);
                AnnualSumValues[AnnualSumValues.Count - 1].Insert(0, Sim.Index);
            }

            //Add values to the annual sums
            for (int i = 0; i < TodaysValues.Count; i++)
            {
                AnnualSumValues[AnnualSumValues.Count - 1][i + 2] += TodaysValues[i];
            }

            //Add values to the annual averages
            for (int i = 0; i < TodaysValues.Count; i++)
            {
                AnnualAverageValues[i + 1] += TodaysValues[i] / noYears;
            }

            TodaysValues.Insert(0, Day);
            TodaysValues.Insert(0, Sim.Index);

            Values.Add(TodaysValues);
        }
    }
}
