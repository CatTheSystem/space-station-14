﻿using System;
using Content.Server.GameObjects.EntitySystems;
using Content.Server.Interfaces.GameObjects;
using Content.Shared.GameObjects;
using Content.Shared.GameObjects.Components.Items;
using Robust.Server.GameObjects;
using Robust.Server.Interfaces.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Server.GameObjects
{
    [RegisterComponent]
    [ComponentReference(typeof(StoreableComponent))]
    public class ItemComponent : StoreableComponent, IAttackHand
    {
        public override string Name => "Item";
        public override uint? NetID => ContentNetIDs.ITEM;
        public override Type StateType => typeof(ItemComponentState);

        private string _equippedPrefix;

        public string EquippedPrefix
        {
            get
            {
                return _equippedPrefix;
            }
            set
            {
                Dirty();
                _equippedPrefix = value;
            }
        }

        public void RemovedFromSlot()
        {
            foreach (var component in Owner.GetAllComponents<ISpriteRenderableComponent>())
            {
                component.Visible = true;
            }
        }

        public void EquippedToSlot()
        {
            foreach (var component in Owner.GetAllComponents<ISpriteRenderableComponent>())
            {
                component.Visible = false;
            }
        }

        public override void ExposeData(ObjectSerializer serializer)
        {
            base.ExposeData(serializer);

            serializer.DataField(ref _equippedPrefix, "HeldPrefix", null);
        }

        public bool AttackHand(AttackHandEventArgs eventArgs)
        {
            var hands = eventArgs.User.GetComponent<IHandsComponent>();
            hands.PutInHand(this, hands.ActiveIndex, fallback: false);
            return true;
        }

        [Verb]
        public sealed class PickUpVerb : Verb<ItemComponent>
        {
            protected override string GetText(IEntity user, ItemComponent component)
            {
                if (user.TryGetComponent(out HandsComponent hands) && hands.IsHolding(component.Owner))
                {
                    return "Pick Up (Already Holding)";
                }
                return "Pick Up";
            }

            protected override VerbVisibility GetVisibility(IEntity user, ItemComponent component)
            {
                if (user.TryGetComponent(out HandsComponent hands) && hands.IsHolding(component.Owner))
                {
                    return VerbVisibility.Disabled;
                }

                return VerbVisibility.Visible;
            }

            protected override void Activate(IEntity user, ItemComponent component)
            {
                if (user.TryGetComponent(out HandsComponent hands) && !hands.IsHolding(component.Owner))
                {
                    hands.PutInHand(component);
                }
            }
        }

        public override ComponentState GetComponentState()
        {
            return new ItemComponentState(EquippedPrefix);
        }

        public void Fumble()
        {
            if (Owner.TryGetComponent<PhysicsComponent>(out var physicsComponent))
            {
                physicsComponent.LinearVelocity += RandomOffset();
            }
        }

        private Vector2 RandomOffset()
        {
            return new Vector2(RandomOffset(), RandomOffset());
            float RandomOffset()
            {
                var size = 15.0F;
                return (new Random().NextFloat() * size) - size / 2;
            }
        }
    }
}
