﻿using System;
using Content.Server.GameObjects.EntitySystems;
using Content.Shared.GameObjects.Components.VendingMachines;
using Content.Shared.VendingMachines;
using Robust.Server.GameObjects.Components.UserInterface;
using Robust.Server.Interfaces.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Components.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timers;
using Robust.Shared.Utility;
using System.Collections.Generic;
using Content.Server.GameObjects.Components.Power;
using Robust.Server.GameObjects;
using Robust.Shared.Log;

namespace Content.Server.GameObjects.Components.VendingMachines
{
    [RegisterComponent]
    [ComponentReference(typeof(IActivate))]
    public class VendingMachineComponent : SharedVendingMachineComponent, IActivate, IExamine, IBreakAct
    {
        private AppearanceComponent _appearance;
        private BoundUserInterface _userInterface;
        private PowerDeviceComponent _powerDevice;

        private bool _ejecting = false;
        private TimeSpan _animationDuration = TimeSpan.Zero;
        private string _packPrototypeId;
        private string _description;
        private string _spriteName;

        private bool Powered => _powerDevice.Powered;
        private bool _broken = false;

        public void Activate(ActivateEventArgs eventArgs)
        {
            if(!eventArgs.User.TryGetComponent(out IActorComponent actor))
            {
                return;
            }

            _userInterface.Open(actor.playerSession);
        }

        public override void ExposeData(ObjectSerializer serializer)
        {
            base.ExposeData(serializer);

            serializer.DataField(ref _packPrototypeId, "pack", string.Empty);
        }

        private void InitializeFromPrototype()
        {
            if (string.IsNullOrEmpty(_packPrototypeId)) { return; }
            var prototypeManger = IoCManager.Resolve<IPrototypeManager>();
            if (!prototypeManger.TryIndex(_packPrototypeId, out VendingMachineInventoryPrototype packPrototype))
            {
                return;
            }

            Owner.Name = packPrototype.Name;
            _description = packPrototype.Description;
            _animationDuration = TimeSpan.FromSeconds(packPrototype.AnimationDuration);
            _spriteName = packPrototype.SpriteName;
            if (!string.IsNullOrEmpty(_spriteName))
            {
                var spriteComponent = Owner.GetComponent<SpriteComponent>();
                const string vendingMachineRSIPath = "Buildings/VendingMachines/{0}.rsi";
                spriteComponent.BaseRSIPath = string.Format(vendingMachineRSIPath, _spriteName);
            }

            var inventory = new List<VendingMachineInventoryEntry>();
            foreach(var (id, amount) in packPrototype.StartingInventory)
            {
                inventory.Add(new VendingMachineInventoryEntry(id, amount));
            }
            Inventory = inventory;
        }

        public override void Initialize()
        {
            base.Initialize();
            _appearance = Owner.GetComponent<AppearanceComponent>();
            _userInterface = Owner.GetComponent<ServerUserInterfaceComponent>()
                .GetBoundUserInterface(VendingMachineUiKey.Key);
            _userInterface.OnReceiveMessage += UserInterfaceOnOnReceiveMessage;
            _powerDevice = Owner.GetComponent<PowerDeviceComponent>();
            _powerDevice.OnPowerStateChanged += UpdatePower;
            InitializeFromPrototype();
        }

        public override void OnRemove()
        {
            _appearance = null;
            _powerDevice.OnPowerStateChanged -= UpdatePower;
            _powerDevice = null;
            base.OnRemove();
        }

        private void UpdatePower(object sender, PowerStateEventArgs args)
        {
            var state = args.Powered ? VendingMachineVisualState.Normal : VendingMachineVisualState.Off;
            TrySetVisualState(state);
        }

        private void UserInterfaceOnOnReceiveMessage(BoundUserInterfaceMessage message)
        {
            switch (message)
            {
                case VendingMachineEjectMessage msg:
                    TryEject(msg.ID);
                    break;
                case InventorySyncRequestMessage msg:
                    _userInterface.SendMessage(new VendingMachineInventoryMessage(Inventory));
                    break;
            }
        }

        public void Examine(FormattedMessage message)
        {
            if(_description == null) { return; }
            message.AddText(_description);
        }

        private void TryEject(string id)
        {
            if (_ejecting || _broken)
            {
                return;
            }

            VendingMachineInventoryEntry entry = Inventory.Find(x => x.ID == id);
            if (entry == null)
            {
                FlickDenyAnimation();
                return;
            }

            if (entry.Amount <= 0)
            {
                FlickDenyAnimation();
                return;
            }

            _ejecting = true;
            entry.Amount--;
            _userInterface.SendMessage(new VendingMachineInventoryMessage(Inventory));
            TrySetVisualState(VendingMachineVisualState.Eject);

            Timer.Spawn(_animationDuration, () =>
            {
                TrySetVisualState(VendingMachineVisualState.Normal);
                _ejecting = false;
                Owner.EntityManager.SpawnEntityAt(id, Owner.Transform.GridPosition);
            });
        }

        private void FlickDenyAnimation()
        {
            TrySetVisualState(VendingMachineVisualState.Deny);
            //TODO: This duration should be a distinct value specific to the deny animation
            Timer.Spawn(_animationDuration, () =>
            {
                TrySetVisualState(VendingMachineVisualState.Normal);
            });
        }

        private void TrySetVisualState(VendingMachineVisualState state)
        {
            var finalState = state;
            if (_broken)
            {
                finalState = VendingMachineVisualState.Broken;
            } else if (_ejecting)
            {
                finalState = VendingMachineVisualState.Eject;
            } else if (!Powered)
            {
                finalState = VendingMachineVisualState.Off;
            }
            _appearance.SetData(VendingMachineVisuals.VisualState, finalState);
        }

        public void OnBreak(BreakageEventArgs eventArgs)
        {
            _broken = true;
            TrySetVisualState(VendingMachineVisualState.Broken);
        }
    }
}

