using System.Collections.Generic;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Core.Server
{
    public static class CombatResolver
    {
        public static void Resolve(GameState state, List<GameEvent> events)
        {
            // TODO front phase, back phase, deaths, queued on-death effects
            //lane by lane
        }
    }
}