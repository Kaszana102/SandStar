using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace SandStarEnemy
{
    public class SandStarAI : EnemyAI
    {
        public enum SandStarState
        {
            Moving,
            PRE_JUMP_DELAY,
            Jumping,
            Aiming,
            Attacking,
            Post_Attacking,
            AIMING_FALLING,
            FALLING,
            FOLLOW_PLAYER,
            DEAD,
        }        
        public Vector3 velocity { get; private set; } = Vector3.zero;

        SandStarState state;
        SkinnedMeshRenderer skinnedMeshRenderer;
        float startTime = 0;
        Vector3 targetAttack = Vector3.zero;
        PlayerControllerB targetPlayer = null;

        const float Speed = 25f;
        const float AttackSpeed = 18f;
        const float JumpingTime = 1f;

        float timeSinceHittingPlayer = 0;
        static List<SandStarAI> stars = new List<SandStarAI>();
        Transform starObject;

        //Particles
        ParticleSystem diggingParticles;

        GameObject radarCircle;

        float lastChangedDirection = 0;


        // sounds
        // TODO ADD WHILE FLYING SOUND AND GROUND DIGGING
        AudioSource FlySound, HitGroundSound, JumpSound, TransformSound;

        bool spawnChild = true;
        List<SandStarAI> childStars=null;        

        public override void Start()
        {
            if (spawnChild &&
                IsServer)
            {
                childStars = new List<SandStarAI>();
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(transform.position, out hit, 100f, NavMesh.AllAreas)){
                    Debug.LogError("Sandstar couldn't find a place for children :/");
                }
                for (int i = 0; i < 2; i++) // create two children
                {

                    GameObject gameObject = Object.Instantiate(enemyType.enemyPrefab, hit.position, Quaternion.Euler(Vector3.zero));
                    gameObject.gameObject.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene: true);
                    SandStarAI child = gameObject.GetComponent<SandStarAI>();
                    child.spawnChild = false;
                    
                    childStars.Add(child);
                    
                }
            }

            base.Start();
            transform.localEulerAngles = Vector3.zero;
            skinnedMeshRenderer = transform.Find("SandStar").GetComponent<SkinnedMeshRenderer>();            
            diggingParticles = transform.Find("SandStar").Find("Particles").Find("Digging").GetComponent<ParticleSystem>();

            starObject = transform.Find("SandStar");
            ventAnimationFinished = true;
            stunNormalizedTimer = -1;
            

            this.enemyHP = 2;

            stars.Add(this);            

            radarCircle = transform.Find("Circle").gameObject;
            radarCircle.layer = 14;


            //sounds
            FlySound = transform.Find("Sounds").Find("Fly").GetComponent<AudioSource>();
            HitGroundSound = transform.Find("Sounds").Find("HitGround").GetComponent<AudioSource>();
            JumpSound = transform.Find("Sounds").Find("Jump").GetComponent<AudioSource>();
            TransformSound = transform.Find("Sounds").Find("Transform").GetComponent<AudioSource>();                        

            SetStateServerRPC(SandStarState.Moving);
        }

        // Update is called once per frame
        public override void Update()
        {
            base.Update();
            CheckState();
            timeSinceHittingPlayer -= Time.deltaTime;
            lastChangedDirection-= Time.deltaTime;

            if (radarCircle == null)
            {
                radarCircle = transform.Find("Circle").gameObject; // for some it was null after start????/
            }
            if (radarCircle.layer != 14)
            {
                radarCircle.layer = 14; //MapRadar layer. Move to update, because it didn't always work in start
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if(state == SandStarState.Moving ||
                state == SandStarState.FOLLOW_PLAYER)
            {                
                    CheckForPlayer();                    
            }
        }

        void CheckState()
        {
            switch (state)
            {
                case SandStarState.Moving:
                    Moving();                                        
                    break;
                case SandStarState.Jumping:
                    Jumping();
                    break;
                case SandStarState.Aiming:
                    Aiming();
                    break;
                case SandStarState.Attacking:
                    Attacking();
                    break;
                case SandStarState.Post_Attacking:
                    Attacking();
                    break;
                case SandStarState.AIMING_FALLING:
                    AimingFalling();
                    break;
                case SandStarState.FALLING:
                    Falling();
                    break;
                case SandStarState.FOLLOW_PLAYER:
                    FollowPlayer();                    
                    break;
                case SandStarState.DEAD:
                    Falling();
                    break;
            }
        }     
       

        void CheckForPlayer()
        {
            if (Time.time - startTime > 5)
            {
                PlayerControllerB playerControllerB = GetClosestPlayer(requireLineOfSight: false, cannotBeInShip: true, cannotBeNearShip: true);
                if (playerControllerB != null && Vector3.Distance(playerControllerB.transform.position, transform.position) < 15f)
                {
                    Debug.Log("sand star FOUND TARGET");
                    SetTargetPlayerServerRCP(playerControllerB.playerClientId);                                        
                }

            }
        }        
        [ServerRpc]
        void SetTargetPlayerServerRCP(ulong playerID)
        {
            SetTargetPlayerClientRCP(playerID);
        }
        [ClientRpc]
        void SetTargetPlayerClientRCP(ulong playerID)
        {
            targetPlayer = StartOfRound.Instance.allPlayerScripts[playerID];
            if (state == SandStarState.Moving || state == SandStarState.FOLLOW_PLAYER)
            {
                SetStateClientRPC(SandStarState.PRE_JUMP_DELAY);
            }
        }
                        

        void Moving()
        {            
            // check distance from target node
            if (destination != Vector3.zero)
            {
                if (Vector3.Distance(destination, transform.position) < 15f)
                {
                    // if close enough change for other
                    SetNewTargetDestinationServerRpc();                    
                }
            }
            else
            {
                // get target node from mother star
                GetTargetDestinationServerRpc();
            }
        }

        // should be made async
        static GameObject GetClosestNodeFromPosition(Vector3 pos)
        {
            GameObject closest = null;
            float minDistance = float.MaxValue;
            foreach(GameObject node in roamPlanet.unsearchedNodes)
            {
                float distance = Vector3.Distance(pos, node.transform.position);
                if(distance < minDistance)
                {
                    minDistance = distance;
                    closest = node;
                }
            }
            
            return closest;
        }

        static AISearchRoutine roamPlanet = new AISearchRoutine();
        static GameObject prevNode=null;
        static int motherStarIndex = 0;
        static bool updateMother=false;

        [ServerRpc(RequireOwnership = false)]
        static void SetNewMother(ulong starID)
        {
            for(int i = 0; i < stars.Count; i++)
            {
                var star = stars[i];
                if (star.NetworkObjectId == starID)
                {
                    motherStarIndex = i ;
                    updateMother = true;
                    break;
                }
            }

        }

        static void SetNewMother()
        {
            for (int i = 0; i < stars.Count; i++)
            {
                var star = stars[i];
                if (star.state != SandStarState.DEAD)
                {
                    motherStarIndex = i;
                    updateMother = true;
                }
            }
        }


        /// <summary>
        /// Select new target node, and pass it to every star
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        static void SetNewTargetDestinationServerRpc()
        {
            if(stars[motherStarIndex].state == SandStarState.DEAD)
            {
                SetNewMother();
            }

            if (updateMother)
            {
                roamPlanet.currentTargetNode = null;
                roamPlanet.unsearchedNodes = null;
                prevNode = null;
                updateMother = false;
            }

            if (roamPlanet.currentTargetNode != null)
            {
                // remove actual node
                if(roamPlanet.unsearchedNodes == null)
                {
                    Debug.LogError("Sandstar: roamPlanet.unsearchedNodes is null");
                }
                else
                {
                    roamPlanet.unsearchedNodes.Remove(roamPlanet.currentTargetNode);
                    
                }
                prevNode = roamPlanet.currentTargetNode;
            }

            // check if there are any other nodes
            if (roamPlanet.unsearchedNodes == null || roamPlanet.unsearchedNodes.Count <= 0)
            {
                roamPlanet.unsearchedNodes =  stars[motherStarIndex].allAINodes.ToList();
            }
            
            
            
            if (prevNode == null)
            {
                roamPlanet.currentTargetNode = GetClosestNodeFromPosition(stars[motherStarIndex].transform.position);
            }
            else
            {
                roamPlanet.currentTargetNode = GetClosestNodeFromPosition(prevNode.transform.position);
            }

            Debug.Log("Sandstar: New target position: " + roamPlanet.currentTargetNode.transform.position);

            foreach(SandStarAI star in stars)
            {
                star.SetNewTargetDestinationClientRpc(roamPlanet.currentTargetNode.transform.position);
            }

        }

        /// <summary>
        /// Set targetNode to target star
        /// </summary>        
        [ClientRpc]
        void SetNewTargetDestinationClientRpc(Vector3 targetPos)
        {
            destination = targetPos;            
        }

        [ServerRpc(RequireOwnership = false)]
        static void GetTargetDestinationServerRpc()
        {
            // you are first star
            if(roamPlanet.currentTargetNode == null)
            {
                SetNewTargetDestinationServerRpc();
            }
            else
            {                
                foreach (SandStarAI star in stars)
                {
                    star.SetNewTargetDestinationClientRpc(roamPlanet.currentTargetNode.transform.position);
                }                
            }
        }

        void AlertOtherStars()
        {
            if (!targetPlayer.isInHangarShipRoom)
            {
                foreach (SandStarAI star in stars)
                {
                    if (star != this
                        &&
                        Vector3.Distance(star.transform.position, transform.position) < 20f)
                    {
                        star.SetTargetPlayerServerRCP(targetPlayer.playerClientId);
                    }
                }
            }
        }

        Coroutine preJumping = null;
        IEnumerator PreJumping()
        {
            float randomTimeOffset = Random.value;
            yield return new WaitForSeconds(randomTimeOffset);
            SetStateServerRPC(SandStarState.Jumping);
        }

        float targetHeight = 0;
        float startHeight = 0;        
        void Jumping()
        {
            targetAttack = targetPlayer.transform.position + new Vector3(0, 1, 0);
            float perc = (Time.time - startTime) / JumpingTime;
            if (perc > 1)
            {
                SetStateServerRPC(SandStarState.Aiming);
                AlertOtherStars();
            }
            else
            {
                float yPos = Mathf.Lerp(startHeight, targetHeight, Mathf.Sqrt(perc));
                Vector3 pos = transform.position;
                pos.y = yPos;
                transform.position = pos;

                starObject.transform.localPosition = Vector3.Lerp(new Vector3(0, -3, 0),Vector3.zero, Mathf.Sqrt(perc));
            }
            transform.LookAt(targetAttack); // not working?
        }

        float[] shapeTimes = { 0.25f, 0.25f, 0.25f };
        int shapeKeyIndex = 0;
        Quaternion attackRotation;
        void Aiming()
        {
            float perc = (Time.time - startTime) / shapeTimes[shapeKeyIndex];

            if (perc > 1)
            {

                if (shapeKeyIndex > 0)
                {
                    skinnedMeshRenderer.SetBlendShapeWeight(shapeKeyIndex - 1, 0);
                    skinnedMeshRenderer.SetBlendShapeWeight(shapeKeyIndex, 100);
                }
                else
                {
                    skinnedMeshRenderer.SetBlendShapeWeight(0, 100);
                }

                shapeKeyIndex++;
                startTime = Time.time;
                if (shapeKeyIndex >= 3)
                {
                    transform.LookAt(targetAttack);
                    transform.localRotation *= Quaternion.Euler(90, 0, 0);
                    attackRotation = transform.rotation;
                    SetStateServerRPC(SandStarState.Attacking);
                }
            }
            else
            {
                if (shapeKeyIndex > 0)
                {
                    skinnedMeshRenderer.SetBlendShapeWeight(shapeKeyIndex, perc * 100);
                    skinnedMeshRenderer.SetBlendShapeWeight(shapeKeyIndex - 1, 100 - perc * 100);
                }
                else
                {
                    skinnedMeshRenderer.SetBlendShapeWeight(0, perc * 100);
                }
            }

            transform.LookAt(targetAttack);
            transform.localRotation *= Quaternion.Euler(90, 0, 0);

        }

        Vector3 direction = Vector3.zero;
        float attackingStartTime = 0;
        void Attacking()
        {
            if (direction == Vector3.zero)
            {

            }            
            transform.position += direction * Time.deltaTime * AttackSpeed;

            transform.rotation = attackRotation;
            transform.localRotation *= Quaternion.Euler(0, 180*(Time.time - attackingStartTime), 0);            

            if(Time.time - attackingStartTime > 3f)
            {
                SetStateServerRPC(SandStarState.AIMING_FALLING);
            }
        }

        Coroutine attackforTimeCoroutine;
        public void StopAttacking()
        {
            if (state == SandStarState.Attacking || state == SandStarState.FALLING)
            {
                SetStateServerRPC(SandStarState.Post_Attacking);
                attackforTimeCoroutine=StartCoroutine(AttackForTime(0.25f));
                HitGroundSound.Play();
            }
        }

        Quaternion startRot;
        Quaternion endRot = Quaternion.EulerRotation(180, 0, 0);
        float startAiming;
        const float AimingTime = 1f;
        void AimingFalling()
        {
            transform.localRotation = Quaternion.Lerp(startRot, endRot, (Time.time - startAiming) / AimingTime);
            if(Time.time - startAiming > AimingTime)
            {
                transform.localRotation = endRot;
                SetStateServerRPC(SandStarState.FALLING);
            }
        }

        void Falling()
        {
            transform.position -= new Vector3(0, 3, 0) * Time.deltaTime;
        }
        protected IEnumerator AttackForTime(float duration)
        {
            float startTime = Time.time;
            while (Time.time < startTime + duration)
            {
                yield return null;
            }
            
            SetStateServerRPC(SandStarState.FOLLOW_PLAYER);
        }

        void FollowPlayer()
        {
            if(Time.time > startTime + 10f)
            {
                SetStateServerRPC(SandStarState.Moving);
            }
        }

        [ServerRpc]
        private void SetStateServerRPC(SandStarState state)
        {
            SetStateClientRPC(state);
        }

        [ClientRpc]
        void SetStateClientRPC(SandStarState newState)
        {
            state = newState;
            startTime = Time.time;

            switch (state)
            {
                case SandStarState.Moving:
                    transform.localEulerAngles = Vector3.zero;
                    velocity = Vector3.zero;
                    diggingParticles.Play();
                    this.agent.Warp(transform.position);
                    this.agent.updatePosition = true;                    
                    this.agent.updateRotation = true;                                        
                    skinnedMeshRenderer.SetBlendShapeWeight(2, 0);
                    starObject.transform.localPosition = new Vector3(0, -3, 0);

                    this.moveTowardsDestination = true;
                    this.movingTowardsTargetPlayer = false;                    

                    break;
                case SandStarState.PRE_JUMP_DELAY:
                    preJumping=StartCoroutine(PreJumping());
                    break;
                case SandStarState.Jumping:
                    this.moveTowardsDestination = false;
                    diggingParticles.Stop();                    
                    this.agent.updatePosition = false;
                    this.agent.updateRotation= false;
                    startHeight = transform.position.y;
                    targetHeight = startHeight + 4;
                    JumpSound.Play(0.1f);
                    StopCoroutine(preJumping);
                    break;
                case SandStarState.Aiming:
                    shapeKeyIndex = 0;
                    direction = (targetAttack - transform.position).normalized;
                    SetNewMother(NetworkObjectId);
                    TransformSound.Play();
                    break;
                case SandStarState.Attacking:
                    attackingStartTime= Time.time;
                    FlySound.Play();
                    break;
                case SandStarState.FOLLOW_PLAYER:                    
                    transform.localEulerAngles = Vector3.zero;
                    velocity = Vector3.zero;
                    diggingParticles.Play();
                    this.agent.Warp(transform.position);
                    this.agent.updatePosition = true;
                    this.agent.updateRotation = true;
                    skinnedMeshRenderer.SetBlendShapeWeight(2, 0);
                    starObject.transform.localPosition = new Vector3(0, -3, 0);

                    this.moveTowardsDestination = true;
                    this.movingTowardsTargetPlayer = false;
                    
                    StopCoroutine(attackforTimeCoroutine);

                    SetMovingTowardsTargetPlayer(targetPlayer);
                    break;
                case SandStarState.DEAD:
                    skinnedMeshRenderer.SetBlendShapeWeight(2, 0);
                    this.agent.Warp(transform.position);
                    this.agent.updatePosition = false;
                    this.agent.updateRotation = false;

                    break;
            }
        }


        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            Debug.Log("Star Collided with player!");
            if (playerControllerB != null && timeSinceHittingPlayer <= 0)
            {
                timeSinceHittingPlayer = 0.5f;
                playerControllerB.DamagePlayer(20, hasDamageSFX: true, callRPC: true, CauseOfDeath.Stabbing, 2);
                //playerControllerB.JumpToFearLevel(1f);
            }
        }        

        public override void OnDestroy()
        {            
            base.OnDestroy();
            stars.Remove(this);            

            foreach(var child in childStars)
            {
                Destroy(child);
            }

            if (stars.Count == 0)
            {
                roamPlanet = new AISearchRoutine(); // Cleanup
            }
        }


        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            this.enemyHP -= force;
            if(enemyHP <= 0)
            {
                SetStateServerRPC(SandStarState.DEAD);
            }
        }
    }

}


