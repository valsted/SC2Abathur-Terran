using Abathur.Constants;
using Abathur.Core;
using Abathur.Model;
using Abathur.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Abathur.Constants.BlizzardConstants;

namespace SC2Abathur.Modules.Tactics
{
    /* Initial conditions
     * 12 Workers
     * 15 Pop cap
     */

    public class OpenerModule : IReplaceableModule
    {
        /* Target:
         * 
         * 12 x SCVs
         *  2 x Supply depot 
         *  2 x Barracks
         *  5 x Marines
         *  
         *  Possible after:
         *  1 x Refinery
         *  3 x SCVs
         *  
         * // TODO:  1 x Orbital Command => Mules?
         */

        public bool Completed { get; set; }

        private readonly IIntelManager intelManager;
        private readonly IProductionManager productionManager;

        public OpenerModule(IIntelManager intelManager, IProductionManager productionManager)
        {
            this.intelManager = intelManager;
            this.productionManager = productionManager;
        }

        public void Initialize() { }

        public void OnStart()
        {
            // Register callback to check if our build is done
            intelManager.Handler.RegisterHandler(Case.UnitAddedSelf, CheckOpeningCompleted);
            intelManager.Handler.RegisterHandler(Case.StructureAddedSelf, CheckOpeningCompleted);
            intelManager.Handler.RegisterHandler(Case.WorkerAddedSelf, CheckOpeningCompleted);

            // Init Production!
            productionManager.QueueUnit(Unit.SCV); // 13
            productionManager.QueueUnit(Unit.SCV); // 14
            productionManager.QueueUnit(Unit.SupplyDepot, spacing: 2);
            productionManager.QueueUnit(Unit.Barracks, spacing: 2);
            productionManager.QueueUnit(Unit.SCV); // 15
            productionManager.QueueUnit(Unit.SCV); // 16
            productionManager.QueueUnit(Unit.Barracks, spacing: 2);
            productionManager.QueueUnit(Unit.SCV); // 17 (just at bonus)
            productionManager.QueueUnit(Unit.SupplyDepot, spacing: 2);
        }

        public void OnStep() 
        {
           // Let productionManager do it's thing
        }

        public void OnGameEnded()
        {
            Completed = false;
        }

        public void OnRestart()
        {
            Completed = false;
            intelManager.Handler.DeregisterHandler(CheckOpeningCompleted);
        }

        public void OnAdded() => OnStart();
        public void OnRemoved() => OnRestart();


        public void CheckOpeningCompleted(IUnit unit)
        {
            // TODO: Potentially rush-vulnerable hardcoded check
            bool SCVs = intelManager.WorkersSelf().Count() >= 16;
            bool barracks = intelManager.StructuresSelf(Unit.Barracks).Count() >= 2;
            bool supplyDepots = intelManager.StructuresSelf(Unit.SupplyDepot).Count() >= 2;
            Completed = SCVs && barracks && supplyDepots;
        }
    }
}
