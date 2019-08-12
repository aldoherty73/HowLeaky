using HowLeaky.CustomAttributes;
using HowLeaky.DataModels;
using HowLeaky.InputModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HowLeaky.ModelControllers
{
    public class DINNitrateController : NitrateController
    {
        public DINNitrateController(Simulation sim) : base(sim) { }

        [Output("Nitrogen Application", "kg/ha")]
        public double NitrogenApplication { get; set; }

        [Output("Mineralisation")]
        public double Mineralisation { get; set; }

        [Output("Crop Use", "Plant")]
        public double CropUsePlant { get; set; }

        [Output("Crop Use", "Ratoon")]
        public double CropUseRatoon { get; set; }

        [Output("Crop Use", "Actual")]
        public double CropUseActual { get; set; }

        [Output("Denitrification")]
        public double Denitrification { get; set; }

        [Output("Excess N", "kg/ha")]
        public double ExcessN { get; set; }

        [Output("Vol of sat", "%")]
        public double PropVolSat { get; set; }

        [Output("DIN Drainage", "")]
        public double DINDrainage { get; set; }

        bool Saturated = false;
        double NApplication;
        double YesterdaysRunoff = 0;
        StageType StageType;

        //public NitrateInputModel InputModel { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sim"></param>
        public DINNitrateController(Simulation sim, List<InputModel> inputModels) : base(sim)
        {
            InputModel = (DINNitrateInputModel)inputModels[0];

            InitOutputModel();
        }

        public override  InputModel GetInputModel()
        {
            return this.InputModel;
        }

        /// <summary>
        /// 
        /// </summary>
        public override void Initialise()
        {
            ((DINNitrateInputModel)InputModel).Plant.CalcDaily();
            ((DINNitrateInputModel)InputModel).Ratoon.CalcDaily();

            //NApplication = InputModel.NitrogenApplication / 365 * InputModel.NitrogenFrequency;
            ExcessN = ((DINNitrateInputModel)InputModel).InitialExcessN;
        }

        /// <summary>
        /// 
        /// </summary>
        public override void Simulate()
        {
            base.Simulate();

            int das = Sim.VegetationController.CurrentCrop.DaysSincePlanting;

            if(das == 1)
            {
                ExcessN = ((DINNitrateInputModel)InputModel).InitialExcessN;
            }

            //Saturated
            Saturated = false;
            if (Sim.Today > Sim.StartDate && Sim.SoilController.Runoff > 0 && YesterdaysRunoff > 0)
            {
                Saturated = true;
            }

            if (Sim.Today > Sim.StartDate)
            {
                //Excess N - calc from yesterdays values
                ExcessN = Math.Max(ExcessN + NitrogenApplication + Mineralisation - CropUseActual - Denitrification - DINDrainage, 0);

                //Denitrification
                Denitrification = 0;
                if (Saturated)
                {
                    Denitrification = ((DINNitrateInputModel)InputModel).Denitrification * ExcessN;
                }
            }

            //Stage
            if (das == 0)
            {
                StageType = StageType.Fallow;
            }
            else if (das > 0 && das < ((DINNitrateInputModel)InputModel).MainStemDuration)
            {
                StageType = StageType.Plant;
            }
            else if (das > 0 && das > ((DINNitrateInputModel)InputModel).MainStemDuration)
            {
                StageType = StageType.Ratoon;
            }

            //Applied N
            NitrogenApplication = 0;
            //if (StageType != StageType.Fallow && (Sim.Today - Sim.StartDate).Days % (int)InputModel.NitrogenFrequency == 0)
            //{
            //    NitrogenApplication = NApplication;
            //}
            if(InputModel.DissolvedNinRunoff.FertilizerInputDateSequences.ContainsDate(Sim.Today))
            {
                NitrogenApplication = InputModel.DissolvedNinRunoff.FertilizerInputDateSequences.ValueAtDate(Sim.Today);
            }

            //Mineralisation
            Mineralisation = 0;
            if (StageType == StageType.Fallow)
            {
                //Mineralisation = InputModel.Mineralisation / 365;
                Mineralisation = Math.Min(Sim.SoilController.InputModel.OrganicCarbon * ((DINNitrateInputModel)InputModel).CNSlope, ((DINNitrateInputModel)InputModel).CNMax) / 365;
            }

            //Crop use
            CropUseActual = 0;
            CropUsePlant = (1 / (1 + (Math.Exp((das - ((DINNitrateInputModel)InputModel).Plant.A) * (-((DINNitrateInputModel)InputModel).Plant.B))))) * ((DINNitrateInputModel)InputModel).Plant.Daily;
            CropUseRatoon = (1 / (1 + (Math.Exp((das - ((DINNitrateInputModel)InputModel).Ratoon.A) * (-((DINNitrateInputModel)InputModel).Ratoon.B))))) * ((DINNitrateInputModel)InputModel).Ratoon.Daily;

            if (StageType == StageType.Plant)
            {
                CropUseActual = CropUsePlant;
            }
            else if (StageType == StageType.Ratoon)
            {
                CropUseActual = CropUseRatoon;
            }

            //Vol of sat
            PropVolSat = Sim.SoilController.DeepDrainage / Sim.SoilController.VolSat;

            //DIN Drainage
            DINDrainage = PropVolSat * ExcessN * ((DINNitrateInputModel)InputModel).NitrateDrainageRetention;

            YesterdaysRunoff = Sim.SoilController.Runoff;
        }
    }
}

