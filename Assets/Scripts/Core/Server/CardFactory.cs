using Game.Cards;
using Game.Core.State;

namespace Game.Core.Server
{
    /// <summary>
    /// Turns a CardDefinition (the static template asset) into a CardInstance
    /// (a live copy in a match). Owns instance-id allocation via
    /// GameState.NextCardInstanceId and seeds the instance's starting stats.
    /// </summary>
    public static class CardFactory
    {
        /// <summary>
        /// Create a live instance of a card. Returns false with a human-readable
        /// error instead of throwing, so a server can reject bad input without
        /// crashing the match. artId picks an alternate art skin; null = default.
        /// </summary>
        public static bool TryCreate(GameState state, CardDefinition def, int ownerId,
                                     out CardInstance card, out string error, string artId = null)
        {
            card = null;

            if (state == null) { error = "GameState is null"; return false; }
            if (def == null) { error = "CardDefinition is null"; return false; }
            if (string.IsNullOrWhiteSpace(def.CardId))
            {
                error = $"CardDefinition '{def.name}' has an empty CardId";
                return false;
            }
            if (ownerId < 0 || ownerId >= state.Players.Length)
            {
                error = $"ownerId {ownerId} is out of range (players: 0..{state.Players.Length - 1})";
                return false;
            }

            var guy = def as GuyCardDefinition;
            card = new CardInstance
            {
                InstanceId = state.NextCardInstanceId++,
                DefinitionId = def.CardId,
                OwnerId = ownerId,
                ArtId = string.IsNullOrWhiteSpace(artId) ? null : artId,
                CurrentCost = def.EnergyCost,
                CurrentAttack = guy?.BaseAttack ?? 0,
                CurrentHealth = guy?.BaseHealth ?? 0,
                KillRewardGold = guy?.KillRewardGold ?? 0,
            };

            // Deep-copy abilities so per-instance upgrades never mutate the shared asset.
            if (guy?.Abilities != null)
                foreach (var ability in guy.Abilities)
                    if (ability != null && !string.IsNullOrWhiteSpace(ability.Id))
                        card.Abilities.Add(ability.Clone());

            error = null;
            return true;
        }

        /// <summary>Throwing convenience wrapper for contexts where failure is a bug.</summary>
        public static CardInstance Create(GameState state, CardDefinition def, int ownerId, string artId = null)
        {
            if (!TryCreate(state, def, ownerId, out var card, out var error, artId))
                throw new System.InvalidOperationException($"CardFactory.Create failed: {error}");
            return card;
        }
    }
}
