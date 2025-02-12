﻿using System;
using Content.Server.GameObjects.Components;
using Content.Server.GameObjects.Components.Movement;
using Content.Server.Interfaces.GameObjects.Components.Movement;
using Content.Shared.Audio;
using Content.Shared.Maps;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Server.GameObjects.EntitySystems;
using Robust.Server.Interfaces.Player;
using Robust.Server.Interfaces.Timing;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Components.Transform;
using Robust.Shared.GameObjects.Systems;
using Robust.Shared.Input;
using Robust.Shared.Interfaces.GameObjects.Components;
using Robust.Shared.Interfaces.Map;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Players;
using Robust.Shared.Prototypes;
using Content.Server.GameObjects.Components.Sound;
using Content.Shared.GameObjects.Components.Inventory;
using Robust.Shared.Log;

namespace Content.Server.GameObjects.EntitySystems
{
    [UsedImplicitly]
    internal class MoverSystem : EntitySystem
    {
#pragma warning disable 649
        [Dependency] private readonly IPauseManager _pauseManager;
        [Dependency] private readonly IPrototypeManager _prototypeManager;
        [Dependency] private readonly ITileDefinitionManager _tileDefinitionManager;
        [Dependency] private readonly IMapManager _mapManager;
#pragma warning restore 649

        private AudioSystem _audioSystem;
        private Random _footstepRandom;

        private const float StepSoundMoveDistanceRunning = 2;
        private const float StepSoundMoveDistanceWalking = 1.5f;

        /// <inheritdoc />
        public override void Initialize()
        {
            EntityQuery = new TypeEntityQuery(typeof(IMoverComponent));
            
            var moveUpCmdHandler = InputCmdHandler.FromDelegate(
                session => HandleDirChange(session, Direction.North, true),
                session => HandleDirChange(session, Direction.North, false));
            var moveLeftCmdHandler = InputCmdHandler.FromDelegate(
                session => HandleDirChange(session, Direction.West, true),
                session => HandleDirChange(session, Direction.West, false));
            var moveRightCmdHandler = InputCmdHandler.FromDelegate(
                session => HandleDirChange(session, Direction.East, true),
                session => HandleDirChange(session, Direction.East, false));
            var moveDownCmdHandler = InputCmdHandler.FromDelegate(
                session => HandleDirChange(session, Direction.South, true),
                session => HandleDirChange(session, Direction.South, false));
            var runCmdHandler = InputCmdHandler.FromDelegate(
                session => HandleRunChange(session, true),
                session => HandleRunChange(session, false));

            var input = EntitySystemManager.GetEntitySystem<InputSystem>();

            input.BindMap.BindFunction(EngineKeyFunctions.MoveUp, moveUpCmdHandler);
            input.BindMap.BindFunction(EngineKeyFunctions.MoveLeft, moveLeftCmdHandler);
            input.BindMap.BindFunction(EngineKeyFunctions.MoveRight, moveRightCmdHandler);
            input.BindMap.BindFunction(EngineKeyFunctions.MoveDown, moveDownCmdHandler);
            input.BindMap.BindFunction(EngineKeyFunctions.Run, runCmdHandler);

            SubscribeEvent<PlayerAttachSystemMessage>(PlayerAttached);
            SubscribeEvent<PlayerDetachedSystemMessage>(PlayerDetached);

            _footstepRandom = new Random();
            _audioSystem = EntitySystemManager.GetEntitySystem<AudioSystem>();
        }

        private static void PlayerAttached(object sender, PlayerAttachSystemMessage ev)
        {
            if (ev.Entity.HasComponent<IMoverComponent>())
            {
                ev.Entity.RemoveComponent<IMoverComponent>();
            }
            ev.Entity.AddComponent<PlayerInputMoverComponent>();
        }

        private static void PlayerDetached(object sender, PlayerDetachedSystemMessage ev)
        {
            ev.Entity.RemoveComponent<PlayerInputMoverComponent>();
        }

        /// <inheritdoc />
        public override void Shutdown()
        {
            if (EntitySystemManager.TryGetEntitySystem(out InputSystem input))
            {
                input.BindMap.UnbindFunction(EngineKeyFunctions.MoveUp);
                input.BindMap.UnbindFunction(EngineKeyFunctions.MoveLeft);
                input.BindMap.UnbindFunction(EngineKeyFunctions.MoveRight);
                input.BindMap.UnbindFunction(EngineKeyFunctions.MoveDown);
                input.BindMap.UnbindFunction(EngineKeyFunctions.Run);
            }

            base.Shutdown();
        }

        /// <inheritdoc />
        public override void Update(float frameTime)
        {
            foreach (var entity in RelevantEntities)
            {
                if (_pauseManager.IsEntityPaused(entity))
                {
                    continue;
                }
                var mover = entity.GetComponent<IMoverComponent>();
                var physics = entity.GetComponent<PhysicsComponent>();

                UpdateKinematics(entity.Transform, mover, physics);
            }
        }

        private void UpdateKinematics(ITransformComponent transform, IMoverComponent mover, PhysicsComponent physics)
        {
            if (mover.VelocityDir.LengthSquared < 0.001 || !ActionBlockerSystem.CanMove(mover.Owner))
            {
                if (physics.LinearVelocity != Vector2.Zero)
                    physics.LinearVelocity = Vector2.Zero;
            }
            else
            {
                physics.LinearVelocity = mover.VelocityDir * (mover.Sprinting ? mover.SprintMoveSpeed : mover.WalkMoveSpeed);
                transform.LocalRotation = mover.VelocityDir.GetDir().ToAngle();

                // Handle footsteps.
                var distance = transform.GridPosition.Distance(_mapManager, mover.LastPosition);
                mover.StepSoundDistance += distance;
                mover.LastPosition = transform.GridPosition;
                float distanceNeeded;
                if (mover.Sprinting)
                {
                    distanceNeeded = StepSoundMoveDistanceRunning;
                }
                else
                {
                    distanceNeeded = StepSoundMoveDistanceWalking;
                }
                if (mover.StepSoundDistance > distanceNeeded)
                {
                    mover.StepSoundDistance = 0;
                    if (mover.Owner.TryGetComponent<InventoryComponent>(out var inventory)
                        && inventory.TryGetSlotItem<ItemComponent>(EquipmentSlotDefines.Slots.SHOES, out var item) 
                        && item.Owner.TryGetComponent<FootstepModifierComponent>(out var modifier))
                    {
                        modifier.PlayFootstep();
                    }
                    else
                    {
                        PlayFootstepSound(transform.GridPosition);
                    }
                }
            }
        }

        private static void HandleDirChange(ICommonSession session, Direction dir, bool state)
        {
            if(!TryGetAttachedComponent(session as IPlayerSession, out PlayerInputMoverComponent moverComp))
                return;

            moverComp.SetVelocityDirection(dir, state);
        }

        private static void HandleRunChange(ICommonSession session, bool running)
        {
            if(!TryGetAttachedComponent(session as IPlayerSession, out PlayerInputMoverComponent moverComp))
                return;

            moverComp.Sprinting = running;
        }

        private static bool TryGetAttachedComponent<T>(IPlayerSession session, out T component)
            where T: Component
        {
            component = default;
            
            var ent = session.AttachedEntity;

            if (ent == null || !ent.IsValid())
                return false;

            if (!ent.TryGetComponent(out T comp))
                return false;

            component = comp;
            return true;
        }

        private void PlayFootstepSound(GridCoordinates coordinates)
        {
            // Step one: figure out sound collection prototype.
            var grid = _mapManager.GetGrid(coordinates.GridID);
            var tile = grid.GetTileRef(coordinates);

            // If the coordinates have a catwalk, it's always catwalk.
            string soundCollectionName;
            var catwalk = false;
            foreach (var maybeCatwalk in grid.GetSnapGridCell(tile.GridIndices, SnapGridOffset.Center))
            {
                if (maybeCatwalk.Owner.HasComponent<CatwalkComponent>())
                {
                    catwalk = true;
                    break;
                }
            }

            if (catwalk)
            {
                // Catwalk overrides tile sound.s
                soundCollectionName = "footstep_catwalk";
            }
            else
            {
                // Walking on a tile.
                var def = (ContentTileDefinition)_tileDefinitionManager[tile.Tile.TypeId];
                if (def.FootstepSounds == null)
                {
                    // Nothing to play, oh well.
                    return;
                }
                soundCollectionName = def.FootstepSounds;
            }

            // Ok well we know the position of the
            try
            {
                var soundCollection = _prototypeManager.Index<SoundCollectionPrototype>(soundCollectionName);
                var file = _footstepRandom.Pick(soundCollection.PickFiles);
                _audioSystem.Play(file, coordinates);
            }
            catch (UnknownPrototypeException)
            {
                // Shouldn't crash over a sound
                Logger.ErrorS("sound", $"Unable to find sound collection for {soundCollectionName}");
            }
        }
    }
}
