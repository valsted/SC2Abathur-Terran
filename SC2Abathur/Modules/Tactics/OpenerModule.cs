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

            // Marine time
            for (int i = 0; i < 5; i++) {
                productionManager.QueueUnit(Unit.Marine);
            }
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
        }

        public void OnAdded() => OnStart();
        public void OnRemoved() => OnRestart();


        public void CheckOpeningCompleted(IUnit unit)
        {
            bool SCVs = intelManager.UnitsSelf(Unit.SCV).Count() >= 16;
            bool barracks = intelManager.UnitsSelf(Unit.Barracks).Count() >= 2;
            bool supplyDepots = intelManager.UnitsSelf(Unit.SupplyDepot).Count() >= 2;
            bool marines = intelManager.UnitsSelf(Unit.Marine).Count() >= 5;
            Completed = SCVs && barracks && supplyDepots && marines;
        }
    }
}
