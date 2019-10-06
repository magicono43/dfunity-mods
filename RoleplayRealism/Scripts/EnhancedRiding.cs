// Project:         RoleplayRealism mod for Daggerfall Unity (http://www.dfworkshop.net)
// Copyright:       Copyright (C) 2019 Hazelnut
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Author:          Hazelnut

using DaggerfallConnect;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Utility;
using System;
using UnityEngine;

namespace DaggerfallWorkshop.Game
{
    public class EnhancedRiding : MonoBehaviour
    {
        public float LookPitchRatio = 2.6f;
        public float extX = 0.06f;
        public float extW = 0.78f;

        const int nativeScreenHeight = 200;
        const int samples = 16;
        const string horseNeckTextureName = "MRED00I1.CFA";
        const string cartNeckTextureName = "MRED01I1.CFA";

        int sampleIdx = 0;
        float[] terrainAngles = new float[samples];
        ImageData[] horseNeckTextures = new ImageData[4];
        ImageData[] cartNeckTextures = new ImageData[4];

        PlayerMotor playerMotor;
        TransportManager transportManager;
        PlayerMouseLook playerMouseLook;

        GameObject cachedColliderHitObject;


        // Delegate for PlayerSpeedChanger - allows horse running.
        public bool CanRunUnlessRidingCart()
        {
            return !(GameManager.Instance.TransportManager.TransportMode == TransportModes.Cart && playerMotor.IsRiding);
        }

        // Initialise.
        void Start()
        {
            playerMotor = GetComponent<PlayerMotor>();
            if (!playerMotor)
                throw new Exception("PlayerMotor not found.");

            transportManager = GetComponent<TransportManager>();
            if (!transportManager)
                throw new Exception("TransportManager not found.");
            transportManager.DrawHorse = false;

            playerMouseLook = GameManager.Instance.PlayerMouseLook;
            if (!playerMouseLook)
                throw new Exception("PlayerMouseLook not found.");

            GameManager.Instance.SpeedChanger.CanRun = CanRunUnlessRidingCart;

            // Setup appropriate neck textures if availiable.
            for (int i = 0; i < 4; i++)
                horseNeckTextures[i] = ImageReader.GetImageData(horseNeckTextureName, 0, i, true, true);
            for (int i = 0; i < 4; i++)
                cartNeckTextures[i] = ImageReader.GetImageData(cartNeckTextureName, 0, i, true, true);
        }

        // Update the mouse look pitch limit when riding status changes.
        void Update()
        {
            if (!GameManager.IsGamePaused && playerMotor.IsRiding)
            {
                // Sample angle of terrain
                RaycastHit hit1, hit2;
                Physics.Raycast(transform.position, Vector3.down, out hit1);
                Physics.Raycast(transform.position + transform.forward, Vector3.down, out hit2);
                float heightDiff = hit1.point.y - hit2.point.y;
                float angle = Mathf.Atan2(heightDiff, 1) * 100;
                terrainAngles[sampleIdx++] = angle;
                if (sampleIdx >= samples)
                    sampleIdx = 0;
                //Debug.LogFormat("Angle: {0}", angle);
            }
            else
            {
                playerMouseLook.PitchMaxLimit = PlayerMouseLook.PitchMax;
            }
        }

        // Handle trampling civilian NPCs.
        private void OnTriggerEnter(Collider other)
        {
            if (playerMotor.IsRiding && playerMotor.IsRunning)
            {
                PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
                Transform npcTransform = other.gameObject.transform;
                MobilePersonNPC mobileNpc = npcTransform.GetComponent<MobilePersonNPC>();
                if (mobileNpc)
                {
                    Debug.Log("Rode over an NPC trampling them!");
                    if (!mobileNpc.Billboard.IsUsingGuardTexture)
                    {
                        EnemyBlood blood = npcTransform.GetComponent<EnemyBlood>();
                        if (blood)
                            blood.ShowBloodSplash(0, BloodPos());
                        playerEntity.SpawnCityGuards(true);
                    }
                    else
                    {
                        GameObject guard = playerEntity.SpawnCityGuard(mobileNpc.transform.position, mobileNpc.transform.forward);
                        DaggerfallEntityBehaviour guardBehaviour = guard.GetComponent<DaggerfallEntityBehaviour>();
                        HandleCharge(guard, guardBehaviour, transform.forward);
                    }
                    playerEntity.CrimeCommitted = PlayerEntity.Crimes.Assault;  // Nearest to manslaughter
                    mobileNpc.Motor.gameObject.SetActive(false);
                }
            }
        }

        // Handle entity collisions.
        private void OnControllerColliderHit(ControllerColliderHit other)
        {
            if (cachedColliderHitObject != other.gameObject)
            {
                DaggerfallEntityBehaviour hitEntityBehaviour = other.gameObject.GetComponent<DaggerfallEntityBehaviour>();
                if (playerMotor.IsRiding && playerMotor.IsRunning && hitEntityBehaviour)
                    HandleCharge(other.gameObject, hitEntityBehaviour, other.moveDirection);
                else
                    cachedColliderHitObject = other.gameObject;
            }
        }

        // Handle charging into enemies.
        private void HandleCharge(GameObject hitGO, DaggerfallEntityBehaviour hitEntityBehaviour, Vector3 direction)
        {
            if (hitEntityBehaviour.Entity is EnemyEntity)
            {
                EnemyEntity hitEnemyEntity = (EnemyEntity)hitEntityBehaviour.Entity;
                if (!hitEnemyEntity.PickpocketByPlayerAttempted)
                {
                    // Play heavy hit sound.
                    EnemySounds enemySounds = hitGO.GetComponent<EnemySounds>();
                    DaggerfallMobileUnit entityMobileUnit = hitGO.GetComponentInChildren<DaggerfallMobileUnit>();
                    Genders gender;
                    if (entityMobileUnit.Summary.Enemy.Gender == MobileGender.Male || hitEnemyEntity.MobileEnemy.ID == (int)MobileTypes.Knight_CityWatch)
                        gender = Genders.Male;
                    else
                        gender = Genders.Female;
                    enemySounds.PlayCombatVoice(gender, false, true);

                    // Knockback the enemy.
                    EnemyMotor enemyMotor = hitGO.GetComponent<EnemyMotor>();
                    enemyMotor.KnockbackSpeed = 100;
                    enemyMotor.KnockbackDirection = direction;

                    // Handle charge hit damage.
                    hitEnemyEntity.PickpocketByPlayerAttempted = true;
                    DaggerfallEntityBehaviour playerEntityBehaviour = GameManager.Instance.PlayerEntity.EntityBehaviour;
                    int damage = FormulaHelper.CalculateHandToHandMaxDamage(GameManager.Instance.PlayerEntity.Skills.GetLiveSkillValue(DFCareer.Skills.HandToHand));
                    damage = playerMotor.IsRunning ? damage * 2 : damage;
                    hitEntityBehaviour.DamageHealthFromSource(playerEntityBehaviour, damage, true, BloodPos());
                    GameManager.Instance.PlayerEntity.DecreaseFatigue(PlayerEntity.DefaultFatigueLoss * 15);
                    Debug.LogFormat("Charged down a {0} for {1} damage!", hitEntityBehaviour.name, damage);
                }
            }
        }

        private Vector3 BloodPos()
        {
            return playerMotor.transform.position + (playerMotor.transform.forward * 2) + playerMotor.transform.up;
        }

        // Handler for enhanced horse animations.
        void OnGUI()
        {
            if (Event.current.type.Equals(EventType.Repaint) && !GameManager.IsGamePaused)
            {
                ImageData ridingTexture = transportManager.RidingTexture;
                int frameIdx = transportManager.FrameIdx;
                if ((transportManager.TransportMode == TransportModes.Horse || transportManager.TransportMode == TransportModes.Cart) && ridingTexture.texture != null)
                {
                    // Draw horse texture behind other HUD elements & weapons.
                    GUI.depth = 2;
                    // Get horse texture scaling factor. (base on height to avoid aspect ratio issues like fat horses)
                    float horseScaleY = (float)Screen.height / (float)nativeScreenHeight;
                    float horseScaleX = horseScaleY * TransportManager.ScaleFactorX;

                    // Calculate average terrain angle
                    float terrainAngle = 0;
                    for (int i=0; i < samples; i++)
                        terrainAngle += terrainAngles[i];
                    terrainAngle /= samples;
                    // Set min look pitch and calc horse sprite y position adjustment
                    playerMouseLook.PitchMaxLimit = terrainAngle + 18;
                    float yAdj = (playerMouseLook.Pitch - terrainAngle - 10) * LookPitchRatio;

                    // Calculate position for horse texture and draw it.
                    Rect pos = new Rect(
                                    Screen.width / 2f - (ridingTexture.width * horseScaleX) / 2f,
                                    Screen.height - ((ridingTexture.height + yAdj) * horseScaleY),
                                    ridingTexture.width * horseScaleX,
                                    ridingTexture.height * horseScaleY);
                    GUI.DrawTexture(pos, ridingTexture.texture);

                    // Draw additional horse neck if required.
                    float drawBottom = pos.y + pos.height - horseScaleY;
                    if (drawBottom < Screen.height)
                    {
                        ImageData[] neckTextures = (transportManager.TransportMode == TransportModes.Horse) ? horseNeckTextures : cartNeckTextures;
                        if (neckTextures[frameIdx].texture.width == 0 || neckTextures[frameIdx].texture.height == 0)
                        {
                            // Duplicate a section of existing sprite.
                            float yAdjNeck = yAdj / 100;
                            Rect posNeck = new Rect(pos.x, drawBottom, (ridingTexture.width - 14) * horseScaleX, Screen.height - drawBottom + horseScaleY);
                            GUI.DrawTextureWithTexCoords(posNeck, ridingTexture.texture, new Rect(extX, 0.2f - yAdjNeck, extW, yAdjNeck));
                        }
                        else
                        {
                            // Draw a secondary image.
                            float yAdjNeck = yAdj / 20.8f;
                            yAdjNeck = yAdjNeck > 0 ? yAdjNeck : 0.001f;
                            Rect posNeck = new Rect(pos.x, drawBottom, ridingTexture.width * horseScaleX, Screen.height - drawBottom + horseScaleY);
                            GUI.DrawTextureWithTexCoords(posNeck, neckTextures[frameIdx].texture, new Rect(0, 1-yAdjNeck, 1, yAdjNeck));
                        }
                    }
                }
            }
        }

    }
}
