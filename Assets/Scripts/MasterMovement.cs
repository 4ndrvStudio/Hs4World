using System.Collections;
using UnityEngine;
using ARP.APR.Scripts;
using Unity.Netcode;
using Unity.Netcode.Components;
using System.Collections.Generic;

namespace HS4
{
    public struct InputPayload : INetworkSerializable
    {
        public int Tick;
        public Vector3 Move;
        public bool PunchLeft;
        public bool PunchRight;
        public float ReachLeft;
        public float ReachRight;
        public float MouseY;
        public float Jump;
        public Vector3 CamForward;


        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref Move);
            serializer.SerializeValue(ref PunchLeft);
            serializer.SerializeValue(ref PunchRight);
            serializer.SerializeValue(ref ReachLeft);
            serializer.SerializeValue(ref ReachRight);
            serializer.SerializeValue(ref MouseY);
            serializer.SerializeValue(ref Jump);
            serializer.SerializeValue(ref CamForward);
        }
    }

    public struct StatePayload : INetworkSerializable
    {
        public int Tick;
        public Vector3 Position;
        public Quaternion Rotation;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
        }
    }


    public class MasterMovement : NetworkBehaviour
    {
        //Netcode
        NetworkTimer _timer;
        const float k_serverTickRate = 60f;
        const int k_bufferSize = 1024;

        //netcode client
        CircularBuffer<StatePayload> _clientStateBuffer;
        CircularBuffer<InputPayload> _clientInputBuffer;
        InputPayload _lastInputPayload;
        StatePayload _lastServerState;
        StatePayload _lastProcessState;

        //netcode server
        CircularBuffer<StatePayload> _serverStateBuffer;
        Queue<InputPayload> _serverInputQueue;

        [SerializeField] private InputReader _inputReader;

        //Active Ragdoll Player parts
        public GameObject
            //
            Root, Body, Head,
            UpperRightArm, LowerRightArm,
            UpperLeftArm, LowerLeftArm,
            UpperRightLeg, LowerRightLeg,
            UpperLeftLeg, LowerLeftLeg,
            RightFoot, LeftFoot;

        //Rigidbody Hands
        public Rigidbody RightHand, LeftHand;

        //Center of mass point
        public Transform COMP;

        [Header("Hand Dependancies")]
        //Hand Controller Scripts & dependancies
        public HandContact GrabRight;
        public HandContact GrabLeft;

        [Header("Input on this player")]
        //Enable controls
        public bool useControls = true;

        [Header("Player Input Axis")]
        //Player Axis controls
        public string forwardBackward = "Vertical";
        public string leftRight = "Horizontal";
        public string jump = "Jump";
        public string reachLeft = "Fire1";
        public string reachRight = "Fire2";

        [Header("Player Input KeyCodes")]
        //Player KeyCode controls
        public string punchLeft = "q";
        public string punchRight = "e";

        [Header("The Layer Only This Player Is On")]
        //Player layer name
        public string thisPlayerLayer = "Player_1";

        [Header("Movement Properties")]
        //Player properties
        public bool forwardIsCameraDirection = true;
        //Movement
        public float moveSpeed = 10f;
        public float turnSpeed = 6f;
        public float jumpForce = 18f;

        [Header("Balance Properties")]
        //Balance
        public bool autoGetUpWhenPossible = true;
        public bool useStepPrediction = true;
        public float balanceHeight = 2.5f;
        public float balanceStrength = 5000f;
        public float coreStrength = 1500f;
        public float limbStrength = 500f;
        //Walking
        public float StepDuration = 0.2f;
        public float StepHeight = 1.7f;
        public float FeetMountForce = 25f;

        [Header("Reach Properties")]
        //Reach
        public float reachSensitivity = 25f;
        public float armReachStiffness = 2000f;

        [Header("Actions")]
        //Punch
        public bool canBeKnockoutByImpact = true;
        public float requiredForceToBeKO = 20f;
        public bool canPunch = true;
        public float punchForce = 15f;

        [Header("Audio")]
        //Impact sounds
        public float ImpactForce = 10f;
        public AudioClip[] Impacts;
        public AudioClip[] Hits;
        public AudioSource SoundSource;


        //Hidden variables
        private float
            timer, Step_R_timer, Step_L_timer,
            MouseYAxisArms, MouseXAxisArms, MouseYAxisBody;

        private bool
            WalkForward, WalkBackward,
            StepRight, StepLeft, Alert_Leg_Right,
            Alert_Leg_Left, balanced = true, GettingUp,
            ResetPose, isRagdoll, isKeyDown, moveAxisUsed,
            jumpAxisUsed, reachLeftAxisUsed, reachRightAxisUsed;

        [HideInInspector]
        public bool
            jumping, isJumping, inAir,
            punchingRight, punchingLeft;

        private Camera cam;
        private Vector3 Direction;
        private Vector3 CenterOfMassPoint;

        //Active Ragdoll Player Parts Array
        private GameObject[] APR_Parts;

        //Joint Drives on & off
        JointDrive
            //
            BalanceOn, PoseOn, CoreStiffness, ReachStiffness, DriveOff;

        //Original pose target rotation
        Quaternion
            //
            HeadTarget, BodyTarget,
            UpperRightArmTarget, LowerRightArmTarget,
            UpperLeftArmTarget, LowerLeftArmTarget,
            UpperRightLegTarget, LowerRightLegTarget,
            UpperLeftLegTarget, LowerLeftLegTarget;

        [Header("Player Editor Debug Mode")]
        //Debug
        public bool editorDebugMode;



        //-------------------------------------------------------------
        //--Calling Functions
        //-------------------------------------------------------------


        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if(IsOwner)
            {
                cam = CameraController.Instance.cam;
                CameraController.Instance.SetFollow(Root.transform);
                _inputReader.Enable();
            }
            //if(IsOwner || IsServer)
            //{
            //    PlayerSetup()
            //}
        }



        //---Setup---//
        //////////////
        void Awake()
        {
            PlayerSetup();

            _timer = new NetworkTimer(k_serverTickRate);
            _clientInputBuffer = new CircularBuffer<InputPayload>(k_bufferSize);
            _clientStateBuffer = new CircularBuffer<StatePayload>(k_bufferSize);

            _serverStateBuffer = new CircularBuffer<StatePayload>(k_bufferSize);
            _serverInputQueue = new Queue<InputPayload>();
        }

        private void Update()
        {
            _timer.Update(Time.deltaTime);

        }
        void FixedUpdate()
        {
            //_timer.Update(Time.deltaTime);

            while (_timer.ShouldTick())
            {
                HandleUpdateClientTick();
                HandleUpdateServerTick();
            }

        }


        private void HandleUpdateClientTick()
        {
            // x: Input.GetAxisRaw(leftRight)
            // y: Input.GetAxisRaw(forwardBackward)
            if (IsOwner)
            {
                var currentTick = _timer.CurrentTick;
                var bufferIndex = currentTick % k_bufferSize;

                InputPayload inputPayload = new InputPayload()
                {
                    Tick = currentTick,
                    Move = new Vector3(ControllerManager.Instance.MovementJoy.Horizontal, ControllerManager.Instance.MovementJoy.Vertical),
                    PunchLeft = _inputReader.IsLeftPunch,
                    PunchRight = _inputReader.IsRightPunch,
                    ReachLeft = _inputReader.LeftReach,
                    ReachRight = _inputReader.RightReach,
                    MouseY = ControllerManager.Instance.UITouchField.TouchDist.y/10f,
                    Jump = _inputReader.Jump,
                    CamForward = cam.transform.forward
                };

                _clientInputBuffer.Add(inputPayload, bufferIndex);
                SendToServerRpc(inputPayload);
                StatePayload statePayload = ProcessMovement(inputPayload);
                _clientStateBuffer.Add(statePayload, bufferIndex);
            } else
            {
                ProcessMovement(_lastInputPayload);
            }
        }

        private void HandleUpdateServerTick()
        {
            if (!IsServer) return;

            var bufferIndex = -1;
            InputPayload inputPayload = default;
            while (_serverInputQueue.Count > 0)
            {
                inputPayload = _serverInputQueue.Dequeue();
                bufferIndex = inputPayload.Tick % k_bufferSize;

                StatePayload statePayload = ProcessMovement(inputPayload);
                _serverStateBuffer.Add(statePayload, bufferIndex);
            }

            if (bufferIndex == -1) return;
            SendToClientRpc(_serverStateBuffer.Get(bufferIndex), inputPayload);
        }

        [ServerRpc]
        void SendToServerRpc(InputPayload inputPayload)
        {
            _serverInputQueue.Enqueue(inputPayload);
        }

        [ClientRpc]
        private void SendToClientRpc(StatePayload statePayload, InputPayload inputPayload)
        {
            if (IsOwner)
            {
                _lastServerState = statePayload;
            }
            else
            {
                _lastInputPayload = inputPayload;
            }
        }

        private StatePayload ProcessMovement(InputPayload input)
        {

            if (useControls && !inAir)
            {
                PlayerMovement(input);

                if (canPunch)
                {
                   PlayerPunch(input);
                }
            }

            if (useControls)
            {
                PlayerReach(input);
            }

            if (balanced && useStepPrediction)
            {
                StepPrediction();
                CenterOfMass();
            }
            if (!useStepPrediction)
            {
                ResetWalkCycle();
            }

            GroundCheck();
            CenterOfMass();


            Walking();

            if (useControls)
            {
                PlayerRotation(input);
                ResetPlayerPose();

                PlayerGetUpJumping(input);
            }

            return new StatePayload
            {
                Tick = input.Tick,
                Position = transform.position,
                Rotation = transform.rotation
            };
        }
       


        //---Fixed Updates---//
        //////////////////////
        //void FixedUpdate()
        //{

        //    //Walking();

        //    //if (useControls)
        //    //{
        //    //   // PlayerRotation();
        //    //    ResetPlayerPose();

        //    //    PlayerGetUpJumping();
        //    //}
        //}


        //-------------------------------------------------------------
        //--Functions
        //-------------------------------------------------------------



        //---Player Setup--//
        ////////////////////
        void PlayerSetup()
        {
           // cam = Camera.main;

            //Setup joint drives
            BalanceOn = new JointDrive();
            BalanceOn.positionSpring = balanceStrength;
            BalanceOn.positionDamper = 0;
            BalanceOn.maximumForce = Mathf.Infinity;

            PoseOn = new JointDrive();
            PoseOn.positionSpring = limbStrength;
            PoseOn.positionDamper = 0;
            PoseOn.maximumForce = Mathf.Infinity;

            CoreStiffness = new JointDrive();
            CoreStiffness.positionSpring = coreStrength;
            CoreStiffness.positionDamper = 0;
            CoreStiffness.maximumForce = Mathf.Infinity;

            ReachStiffness = new JointDrive();
            ReachStiffness.positionSpring = armReachStiffness;
            ReachStiffness.positionDamper = 0;
            ReachStiffness.maximumForce = Mathf.Infinity;

            DriveOff = new JointDrive();
            DriveOff.positionSpring = 25;
            DriveOff.positionDamper = 0;
            DriveOff.maximumForce = Mathf.Infinity;

            //Setup/reroute active ragdoll parts to array
            APR_Parts = new GameObject[]
            {
				//array index numbers
			
				//0
				Root,
				//1
				Body,
				//2
				Head,
				//3
				UpperRightArm,
				//4
				LowerRightArm,
				//5
				UpperLeftArm,
				//6
				LowerLeftArm,
				//7
				UpperRightLeg,
				//8
				LowerRightLeg,
				//9
				UpperLeftLeg,
				//10
				LowerLeftLeg,
				//11
				RightFoot,
				//12
				LeftFoot
            };

            //Setup original pose for joint drives
            BodyTarget = APR_Parts[1].GetComponent<ConfigurableJoint>().targetRotation;
            HeadTarget = APR_Parts[2].GetComponent<ConfigurableJoint>().targetRotation;
            UpperRightArmTarget = APR_Parts[3].GetComponent<ConfigurableJoint>().targetRotation;
            LowerRightArmTarget = APR_Parts[4].GetComponent<ConfigurableJoint>().targetRotation;
            UpperLeftArmTarget = APR_Parts[5].GetComponent<ConfigurableJoint>().targetRotation;
            LowerLeftArmTarget = APR_Parts[6].GetComponent<ConfigurableJoint>().targetRotation;
            UpperRightLegTarget = APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation;
            LowerRightLegTarget = APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation;
            UpperLeftLegTarget = APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation;
            LowerLeftLegTarget = APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation;
        }



        //---Ground Check---//
        /////////////////////
        void GroundCheck()
        {
            Ray ray = new Ray(APR_Parts[0].transform.position, -APR_Parts[0].transform.up);
            RaycastHit hit;

            //Balance when ground is detected
            if (Physics.Raycast(ray, out hit, balanceHeight, 1 << LayerMask.NameToLayer("Ground")) && !inAir && !isJumping && !reachRightAxisUsed && !reachLeftAxisUsed)
            {
                if (!balanced && APR_Parts[0].GetComponent<Rigidbody>().velocity.magnitude < 1f)
                {
                    if (autoGetUpWhenPossible)
                    {
                        balanced = true;
                    }
                }
            }

            //Fall over when ground is not detected
            else if (!Physics.Raycast(ray, out hit, balanceHeight, 1 << LayerMask.NameToLayer("Ground")))
            {
                if (balanced)
                {
                    balanced = false;
                }
            }


            //Balance on/off
            if (balanced && isRagdoll)
            {
                DeactivateRagdoll();
            }
            else if (!balanced && !isRagdoll)
            {
                ActivateRagdoll();
            }
        }



        //---Step Prediction---//
        ////////////////////////
        void StepPrediction()
        {
            //Reset variables when balanced
            if (!WalkForward && !WalkBackward)
            {
                StepRight = false;
                StepLeft = false;
                Step_R_timer = 0;
                Step_L_timer = 0;
                Alert_Leg_Right = false;
                Alert_Leg_Left = false;
            }

            //Check direction to walk when off balance
            //Backwards
            if (COMP.position.z < APR_Parts[11].transform.position.z && COMP.position.z < APR_Parts[12].transform.position.z)
            {
                WalkBackward = true;
            }
            else
            {
                if (!isKeyDown)
                {
                    WalkBackward = false;
                }
            }

            //Forward
            if (COMP.position.z > APR_Parts[11].transform.position.z && COMP.position.z > APR_Parts[12].transform.position.z)
            {
                WalkForward = true;
            }
            else
            {
                if (!isKeyDown)
                {
                    WalkForward = false;
                }
            }
        }



        //---Reset Walk Cycle---//
        /////////////////////////
        void ResetWalkCycle()
        {
            //Reset variables when not moving
            if (!WalkForward && !WalkBackward)
            {
                StepRight = false;
                StepLeft = false;
                Step_R_timer = 0;
                Step_L_timer = 0;
                Alert_Leg_Right = false;
                Alert_Leg_Left = false;
            }
        }



        //---Player Movement---//
        ////////////////////////
        void PlayerMovement(InputPayload input)
        {
            //Move in camera direction
            if (forwardIsCameraDirection)
            {
                Direction = APR_Parts[0].transform.rotation * new Vector3(input.Move.x, 0.0f, input.Move.y);
                Direction.y = 0f;
                APR_Parts[0].transform.GetComponent<Rigidbody>().velocity = Vector3.Lerp(APR_Parts[0].transform.GetComponent<Rigidbody>().velocity, (Direction * moveSpeed) + new Vector3(0, APR_Parts[0].transform.GetComponent<Rigidbody>().velocity.y, 0), 0.8f);

                if (input.Move.x != 0 || input.Move.y != 0 && balanced)
                {
                    if (!WalkForward && !moveAxisUsed)
                    {
                        WalkForward = true;
                        moveAxisUsed = true;
                        isKeyDown = true;
                    }
                }

                else if (input.Move.x == 0 && input.Move.y == 0)
                {
                    if (WalkForward && moveAxisUsed)
                    {
                        WalkForward = false;
                        moveAxisUsed = false;
                        isKeyDown = false;
                    }
                }
            }

            //Move in own direction
            else
            {
                if (input.Move.y != 0)
                {
                    var v3 = APR_Parts[0].GetComponent<Rigidbody>().transform.forward * (input.Move.y * moveSpeed);
                    v3.y = APR_Parts[0].GetComponent<Rigidbody>().velocity.y;
                    APR_Parts[0].GetComponent<Rigidbody>().velocity = v3;
                }


                if (input.Move.y > 0)
                {
                    if (!WalkForward && !moveAxisUsed)
                    {
                        WalkBackward = false;
                        WalkForward = true;
                        moveAxisUsed = true;
                        isKeyDown = true;

                        if (isRagdoll)
                        {
                            APR_Parts[7].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[7].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                            APR_Parts[8].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[8].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                            APR_Parts[9].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[9].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                            APR_Parts[10].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[10].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                            APR_Parts[11].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[11].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                            APR_Parts[12].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[12].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                        }
                    }
                }

                else if (input.Move.y < 0)
                {
                    if (!WalkBackward && !moveAxisUsed)
                    {
                        WalkForward = false;
                        WalkBackward = true;
                        moveAxisUsed = true;
                        isKeyDown = true;

                        if (isRagdoll)
                        {
                            APR_Parts[7].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[7].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                            APR_Parts[8].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[8].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                            APR_Parts[9].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[9].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                            APR_Parts[10].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[10].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                            APR_Parts[11].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[11].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                            APR_Parts[12].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                            APR_Parts[12].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                        }
                    }
                }

                else if (input.Move.y == 0)
                {
                    if (WalkForward || WalkBackward && moveAxisUsed)
                    {
                        WalkForward = false;
                        WalkBackward = false;
                        moveAxisUsed = false;
                        isKeyDown = false;

                        if (isRagdoll)
                        {
                            APR_Parts[7].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                            APR_Parts[7].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                            APR_Parts[8].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                            APR_Parts[8].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                            APR_Parts[9].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                            APR_Parts[9].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                            APR_Parts[10].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                            APR_Parts[10].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                            APR_Parts[11].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                            APR_Parts[11].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                            APR_Parts[12].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                            APR_Parts[12].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                        }
                    }
                }
            }
        }



        //---Player Rotation---//
        ////////////////////////
        void PlayerRotation(InputPayload input)
        {
            if (forwardIsCameraDirection)
            {
                //Camera Direction
                //Turn with camera
                var lookPos = input.CamForward;
                lookPos.y = 0;
                var rotation = Quaternion.LookRotation(lookPos);
                APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation = Quaternion.Slerp(APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation, Quaternion.Inverse(rotation), Time.fixedDeltaTime * turnSpeed);
            }

            else
            {
                //Self Direction
                //Turn with keys
                if (input.Move.x != 0)
                {
                    APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation = Quaternion.Lerp(APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation, new Quaternion(APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.x, APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.y - (input.Move.x * turnSpeed), APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.w), 6 * Time.fixedDeltaTime);
                }

                //reset turn upon target rotation limit
                if (APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.y < -0.98f)
                {
                    APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.x, 0.98f, APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.w);
                }

                else if (APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.y > 0.98f)
                {
                    APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.x, -0.98f, APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[0].GetComponent<ConfigurableJoint>().targetRotation.w);
                }
            }
        }



        //---Player GetUp & Jumping---//
        ///////////////////////////////
        void PlayerGetUpJumping(InputPayload input)
        {
            if (input.Jump > 0)
            {
                if (!jumpAxisUsed)
                {
                    if (balanced && !inAir)
                    {
                        jumping = true;
                    }

                    else if (!balanced)
                    {
                        DeactivateRagdoll();
                    }
                }

                jumpAxisUsed = true;
            }

            else
            {
                jumpAxisUsed = false;
            }


            if (jumping)
            {
                isJumping = true;

                var v3 = APR_Parts[0].GetComponent<Rigidbody>().transform.up * jumpForce;
                v3.x = APR_Parts[0].GetComponent<Rigidbody>().velocity.x;
                v3.z = APR_Parts[0].GetComponent<Rigidbody>().velocity.z;
                APR_Parts[0].GetComponent<Rigidbody>().velocity = v3;
            }

            if (isJumping)
            {
                timer = timer + Time.fixedDeltaTime;

                if (timer > 0.2f)
                {
                    timer = 0.0f;
                    jumping = false;
                    isJumping = false;
                    inAir = true;
                }
            }
        }



        //---Player Landed---//
        //////////////////////
        public void PlayerLanded()
        {
            if (inAir && !isJumping && !jumping)
            {
                inAir = false;
                ResetPose = true;
            }
        }



        //---Player Reach--//
        ////////////////////
        void PlayerReach(InputPayload input)
        {
            //Body Bending
            if (1 == 1)
            {
                if (MouseYAxisBody <= 0.9f && MouseYAxisBody >= -0.9f)
                {
                    MouseYAxisBody = MouseYAxisBody + (input.MouseY / reachSensitivity);
                }

                else if (MouseYAxisBody > 0.9f)
                {
                    MouseYAxisBody = 0.9f;
                }

                else if (MouseYAxisBody < -0.9f)
                {
                    MouseYAxisBody = -0.9f;
                }

                APR_Parts[1].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(MouseYAxisBody, 0, 0, 1);
            }


            //Reach Left
            if (input.ReachLeft != 0 && !punchingLeft)
            {

                if (!reachLeftAxisUsed)
                {
                    //Adjust Left Arm joint strength
                    APR_Parts[5].GetComponent<ConfigurableJoint>().angularXDrive = ReachStiffness;
                    APR_Parts[5].GetComponent<ConfigurableJoint>().angularYZDrive = ReachStiffness;
                    APR_Parts[6].GetComponent<ConfigurableJoint>().angularXDrive = ReachStiffness;
                    APR_Parts[6].GetComponent<ConfigurableJoint>().angularYZDrive = ReachStiffness;

                    //Adjust body joint strength
                    APR_Parts[1].GetComponent<ConfigurableJoint>().angularXDrive = CoreStiffness;
                    APR_Parts[1].GetComponent<ConfigurableJoint>().angularYZDrive = CoreStiffness;

                    reachLeftAxisUsed = true;
                }

                if (MouseYAxisArms <= 1.2f && MouseYAxisArms >= -1.2f)
                {
                    MouseYAxisArms = MouseYAxisArms + (input.MouseY / reachSensitivity);
                }

                else if (MouseYAxisArms > 1.2f)
                {
                    MouseYAxisArms = 1.2f;
                }

                else if (MouseYAxisArms < -1.2f)
                {
                    MouseYAxisArms = -1.2f;
                }

                //upper  left arm pose
                APR_Parts[5].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(-0.58f - (MouseYAxisArms), -0.88f - (MouseYAxisArms), -0.8f, 1);
            }

            if (input.ReachLeft == 0 && !punchingLeft)
            {
                if (reachLeftAxisUsed)
                {
                    if (balanced)
                    {
                        APR_Parts[5].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                        APR_Parts[5].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                        APR_Parts[6].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                        APR_Parts[6].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;

                        APR_Parts[1].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                        APR_Parts[1].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                    }

                    else if (!balanced)
                    {
                        APR_Parts[5].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                        APR_Parts[5].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                        APR_Parts[6].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                        APR_Parts[6].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                    }

                    ResetPose = true;
                    reachLeftAxisUsed = false;
                }
            }




            //Reach Right
            if (input.ReachRight != 0 && !punchingRight)
            {

                if (!reachRightAxisUsed)
                {
                    //Adjust Right Arm joint strength
                    APR_Parts[3].GetComponent<ConfigurableJoint>().angularXDrive = ReachStiffness;
                    APR_Parts[3].GetComponent<ConfigurableJoint>().angularYZDrive = ReachStiffness;
                    APR_Parts[4].GetComponent<ConfigurableJoint>().angularXDrive = ReachStiffness;
                    APR_Parts[4].GetComponent<ConfigurableJoint>().angularYZDrive = ReachStiffness;

                    //Adjust body joint strength
                    APR_Parts[1].GetComponent<ConfigurableJoint>().angularXDrive = CoreStiffness;
                    APR_Parts[1].GetComponent<ConfigurableJoint>().angularYZDrive = CoreStiffness;

                    reachRightAxisUsed = true;
                }

                if (MouseYAxisArms <= 1.2f && MouseYAxisArms >= -1.2f)
                {
                    MouseYAxisArms = MouseYAxisArms + (input.MouseY / reachSensitivity);
                }

                else if (MouseYAxisArms > 1.2f)
                {
                    MouseYAxisArms = 1.2f;
                }

                else if (MouseYAxisArms < -1.2f)
                {
                    MouseYAxisArms = -1.2f;
                }

                //upper right arm pose
                APR_Parts[3].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(0.58f + (MouseYAxisArms), -0.88f - (MouseYAxisArms), 0.8f, 1);
            }

            if (input.ReachRight == 0 && !punchingRight)
            {
                if (reachRightAxisUsed)
                {
                    if (balanced)
                    {
                        APR_Parts[3].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                        APR_Parts[3].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                        APR_Parts[4].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                        APR_Parts[4].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;

                        APR_Parts[1].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                        APR_Parts[1].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                    }

                    else if (!balanced)
                    {
                        APR_Parts[3].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                        APR_Parts[3].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                        APR_Parts[4].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                        APR_Parts[4].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                    }

                    ResetPose = true;
                    reachRightAxisUsed = false;
                }
            }

        }



        //---Player Punch---//
        /////////////////////
        void PlayerPunch(InputPayload input)
        {

            //punch right
            if (!punchingRight && input.PunchRight)
            {
                punchingRight = true;

                //Right hand punch pull back pose
                APR_Parts[1].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(-0.15f, -0.15f, 0, 1);
                APR_Parts[3].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(-0.62f, -0.51f, 0.02f, 1);
                APR_Parts[4].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(1.31f, 0.5f, -0.5f, 1);
            }

            if (punchingRight && !input.PunchRight)
            {
                punchingRight = false;

                //Right hand punch release pose
                APR_Parts[1].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(-0.15f, 0.15f, 0, 1);
                APR_Parts[3].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(0.74f, 0.04f, 0f, 1);
                APR_Parts[4].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(0.2f, 0, 0, 1);

                //Right hand punch force
                RightHand.AddForce(APR_Parts[0].transform.forward * punchForce, ForceMode.Impulse);

                APR_Parts[1].GetComponent<Rigidbody>().AddForce(APR_Parts[0].transform.forward * punchForce, ForceMode.Impulse);

                StartCoroutine(DelayCoroutine());
                IEnumerator DelayCoroutine()
                {
                    yield return new WaitForSeconds(0.3f);
                    if (!input.PunchRight)
                    {
                        APR_Parts[3].GetComponent<ConfigurableJoint>().targetRotation = UpperRightArmTarget;
                        APR_Parts[4].GetComponent<ConfigurableJoint>().targetRotation = LowerRightArmTarget;
                    }
                }
            }


            //punch left
            if (!punchingLeft && input.PunchLeft)
            {
                punchingLeft = true;

                //Left hand punch pull back pose
                APR_Parts[1].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(-0.15f, 0.15f, 0, 1);
                APR_Parts[5].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(0.62f, -0.51f, 0.02f, 1);
                APR_Parts[6].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(-1.31f, 0.5f, 0.5f, 1);
            }

            if (punchingLeft && !input.PunchLeft)
            {
                punchingLeft = false;

                //Left hand punch release pose
                APR_Parts[1].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(-0.15f, -0.15f, 0, 1);
                APR_Parts[5].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(-0.74f, 0.04f, 0f, 1);
                APR_Parts[6].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(-0.2f, 0, 0, 1);

                //Left hand punch force
                LeftHand.AddForce(APR_Parts[0].transform.forward * punchForce, ForceMode.Impulse);

                APR_Parts[1].GetComponent<Rigidbody>().AddForce(APR_Parts[0].transform.forward * punchForce, ForceMode.Impulse);

                StartCoroutine(DelayCoroutine());
                IEnumerator DelayCoroutine()
                {
                    yield return new WaitForSeconds(0.3f);
                    if (!input.PunchLeft)
                    {
                        APR_Parts[5].GetComponent<ConfigurableJoint>().targetRotation = UpperLeftArmTarget;
                        APR_Parts[6].GetComponent<ConfigurableJoint>().targetRotation = LowerLeftArmTarget;
                    }
                }
            }
        }



        //---Player Walking---//
        ///////////////////////
        void Walking()
        {
            if (!inAir)
            {
                if (WalkForward)
                {
                    //right leg
                    if (APR_Parts[11].transform.position.z < APR_Parts[12].transform.position.z && !StepLeft && !Alert_Leg_Right)
                    {
                        StepRight = true;
                        Alert_Leg_Right = true;
                        Alert_Leg_Left = true;
                    }

                    //left leg
                    if (APR_Parts[11].transform.position.z > APR_Parts[12].transform.position.z && !StepRight && !Alert_Leg_Left)
                    {
                        StepLeft = true;
                        Alert_Leg_Left = true;
                        Alert_Leg_Right = true;
                    }
                }

                if (WalkBackward)
                {
                    //right leg
                    if (APR_Parts[11].transform.position.z > APR_Parts[12].transform.position.z && !StepLeft && !Alert_Leg_Right)
                    {
                        StepRight = true;
                        Alert_Leg_Right = true;
                        Alert_Leg_Left = true;
                    }

                    //left leg
                    if (APR_Parts[11].transform.position.z < APR_Parts[12].transform.position.z && !StepRight && !Alert_Leg_Left)
                    {
                        StepLeft = true;
                        Alert_Leg_Left = true;
                        Alert_Leg_Right = true;
                    }
                }

                //Step right
                if (StepRight)
                {
                    Step_R_timer += Time.fixedDeltaTime;

                    //Right foot force down
                    APR_Parts[11].GetComponent<Rigidbody>().AddForce(-Vector3.up * FeetMountForce * Time.deltaTime, ForceMode.Impulse);

                    //walk simulation
                    if (WalkForward)
                    {
                        APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.x + 0.09f * StepHeight, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.w);
                        APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation.x - 0.09f * StepHeight * 2, APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation.w);

                        APR_Parts[9].GetComponent<ConfigurableJoint>().GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.x - 0.12f * StepHeight / 2, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.w);
                    }

                    if (WalkBackward)
                    {
                        APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.x - 0.00f * StepHeight, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.w);
                        APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation.x - 0.07f * StepHeight * 2, APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation.w);

                        APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.x + 0.02f * StepHeight / 2, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.w);
                    }


                    //step duration
                    if (Step_R_timer > StepDuration)
                    {
                        Step_R_timer = 0;
                        StepRight = false;

                        if (WalkForward || WalkBackward)
                        {
                            StepLeft = true;
                        }
                    }
                }
                else
                {
                    //reset to idle
                    APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation = Quaternion.Lerp(APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation, UpperRightLegTarget, (8f) * Time.fixedDeltaTime);
                    APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation = Quaternion.Lerp(APR_Parts[8].GetComponent<ConfigurableJoint>().targetRotation, LowerRightLegTarget, (17f) * Time.fixedDeltaTime);

                    //feet force down
                    APR_Parts[11].GetComponent<Rigidbody>().AddForce(-Vector3.up * FeetMountForce * Time.deltaTime, ForceMode.Impulse);
                    APR_Parts[12].GetComponent<Rigidbody>().AddForce(-Vector3.up * FeetMountForce * Time.deltaTime, ForceMode.Impulse);
                }


                //Step left
                if (StepLeft)
                {
                    Step_L_timer += Time.fixedDeltaTime;

                    //Left foot force down
                    APR_Parts[12].GetComponent<Rigidbody>().AddForce(-Vector3.up * FeetMountForce * Time.deltaTime, ForceMode.Impulse);

                    //walk simulation
                    if (WalkForward)
                    {
                        APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.x + 0.09f * StepHeight, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.w);
                        APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation.x - 0.09f * StepHeight * 2, APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation.w);

                        APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.x - 0.12f * StepHeight / 2, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.w);
                    }

                    if (WalkBackward)
                    {
                        APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.x - 0.00f * StepHeight, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation.w);
                        APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation.x - 0.07f * StepHeight * 2, APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation.w);

                        APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation = new Quaternion(APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.x + 0.02f * StepHeight / 2, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.y, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.z, APR_Parts[7].GetComponent<ConfigurableJoint>().targetRotation.w);
                    }


                    //Step duration
                    if (Step_L_timer > StepDuration)
                    {
                        Step_L_timer = 0;
                        StepLeft = false;

                        if (WalkForward || WalkBackward)
                        {
                            StepRight = true;
                        }
                    }
                }
                else
                {
                    //reset to idle
                    APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation = Quaternion.Lerp(APR_Parts[9].GetComponent<ConfigurableJoint>().targetRotation, UpperLeftLegTarget, (7f) * Time.fixedDeltaTime);
                    APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation = Quaternion.Lerp(APR_Parts[10].GetComponent<ConfigurableJoint>().targetRotation, LowerLeftLegTarget, (18f) * Time.fixedDeltaTime);

                    //feet force down
                    APR_Parts[11].GetComponent<Rigidbody>().AddForce(-Vector3.up * FeetMountForce * Time.deltaTime, ForceMode.Impulse);
                    APR_Parts[12].GetComponent<Rigidbody>().AddForce(-Vector3.up * FeetMountForce * Time.deltaTime, ForceMode.Impulse);
                }
            }
        }



        //---Activate Ragdoll---//
        /////////////////////////
        public void ActivateRagdoll()
        {
            isRagdoll = true;
            balanced = false;

            //Root
            APR_Parts[0].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
            APR_Parts[0].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
            //head
            APR_Parts[2].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
            APR_Parts[2].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
            //arms
            if (!reachRightAxisUsed)
            {
                APR_Parts[3].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                APR_Parts[3].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                APR_Parts[4].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                APR_Parts[4].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
            }

            if (!reachLeftAxisUsed)
            {
                APR_Parts[5].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                APR_Parts[5].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
                APR_Parts[6].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
                APR_Parts[6].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
            }
            //legs
            APR_Parts[7].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
            APR_Parts[7].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
            APR_Parts[8].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
            APR_Parts[8].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
            APR_Parts[9].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
            APR_Parts[9].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
            APR_Parts[10].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
            APR_Parts[10].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
            APR_Parts[11].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
            APR_Parts[11].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
            APR_Parts[12].GetComponent<ConfigurableJoint>().angularXDrive = DriveOff;
            APR_Parts[12].GetComponent<ConfigurableJoint>().angularYZDrive = DriveOff;
        }




        //---Deactivate Ragdoll---//
        ///////////////////////////
        void DeactivateRagdoll()
        {
            isRagdoll = false;
            balanced = true;

            //Root
            APR_Parts[0].GetComponent<ConfigurableJoint>().angularXDrive = BalanceOn;
            APR_Parts[0].GetComponent<ConfigurableJoint>().angularYZDrive = BalanceOn;
            //head
            APR_Parts[2].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
            APR_Parts[2].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
            //arms
            if (!reachRightAxisUsed)
            {
                APR_Parts[3].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                APR_Parts[3].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                APR_Parts[4].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                APR_Parts[4].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
            }

            if (!reachLeftAxisUsed)
            {
                APR_Parts[5].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                APR_Parts[5].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
                APR_Parts[6].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
                APR_Parts[6].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
            }
            //legs
            APR_Parts[7].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
            APR_Parts[7].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
            APR_Parts[8].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
            APR_Parts[8].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
            APR_Parts[9].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
            APR_Parts[9].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
            APR_Parts[10].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
            APR_Parts[10].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
            APR_Parts[11].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
            APR_Parts[11].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;
            APR_Parts[12].GetComponent<ConfigurableJoint>().angularXDrive = PoseOn;
            APR_Parts[12].GetComponent<ConfigurableJoint>().angularYZDrive = PoseOn;

            ResetPose = true;
        }



        //---Reset Player Pose---//
        //////////////////////////
        void ResetPlayerPose()
        {
            if (ResetPose && !jumping)
            {
                APR_Parts[1].GetComponent<ConfigurableJoint>().targetRotation = BodyTarget;
                APR_Parts[3].GetComponent<ConfigurableJoint>().targetRotation = UpperRightArmTarget;
                APR_Parts[4].GetComponent<ConfigurableJoint>().targetRotation = LowerRightArmTarget;
                APR_Parts[5].GetComponent<ConfigurableJoint>().targetRotation = UpperLeftArmTarget;
                APR_Parts[6].GetComponent<ConfigurableJoint>().targetRotation = LowerLeftArmTarget;

                MouseYAxisArms = 0;

                ResetPose = false;
            }
        }



        //---Calculating Center of mass point---//
        /////////////////////////////////////////
        void CenterOfMass()
        {
            CenterOfMassPoint =

                (APR_Parts[0].GetComponent<Rigidbody>().mass * APR_Parts[0].transform.position +
                 APR_Parts[1].GetComponent<Rigidbody>().mass * APR_Parts[1].transform.position +
                 APR_Parts[2].GetComponent<Rigidbody>().mass * APR_Parts[2].transform.position +
                 APR_Parts[3].GetComponent<Rigidbody>().mass * APR_Parts[3].transform.position +
                 APR_Parts[4].GetComponent<Rigidbody>().mass * APR_Parts[4].transform.position +
                 APR_Parts[5].GetComponent<Rigidbody>().mass * APR_Parts[5].transform.position +
                 APR_Parts[6].GetComponent<Rigidbody>().mass * APR_Parts[6].transform.position +
                 APR_Parts[7].GetComponent<Rigidbody>().mass * APR_Parts[7].transform.position +
                 APR_Parts[8].GetComponent<Rigidbody>().mass * APR_Parts[8].transform.position +
                 APR_Parts[9].GetComponent<Rigidbody>().mass * APR_Parts[9].transform.position +
                 APR_Parts[10].GetComponent<Rigidbody>().mass * APR_Parts[10].transform.position +
                 APR_Parts[11].GetComponent<Rigidbody>().mass * APR_Parts[11].transform.position +
                 APR_Parts[12].GetComponent<Rigidbody>().mass * APR_Parts[12].transform.position)

                /

                (APR_Parts[0].GetComponent<Rigidbody>().mass + APR_Parts[1].GetComponent<Rigidbody>().mass +
                 APR_Parts[2].GetComponent<Rigidbody>().mass + APR_Parts[3].GetComponent<Rigidbody>().mass +
                 APR_Parts[4].GetComponent<Rigidbody>().mass + APR_Parts[5].GetComponent<Rigidbody>().mass +
                 APR_Parts[6].GetComponent<Rigidbody>().mass + APR_Parts[7].GetComponent<Rigidbody>().mass +
                 APR_Parts[8].GetComponent<Rigidbody>().mass + APR_Parts[9].GetComponent<Rigidbody>().mass +
                 APR_Parts[10].GetComponent<Rigidbody>().mass + APR_Parts[11].GetComponent<Rigidbody>().mass +
                 APR_Parts[12].GetComponent<Rigidbody>().mass);

            COMP.position = CenterOfMassPoint;
        }



        //-------------------------------------------------------------
        //--Debug
        //-------------------------------------------------------------



        //---Editor Debug Mode---//
        //////////////////////////
        void OnDrawGizmos()
        {
            if (editorDebugMode)
            {
                Debug.DrawRay(Root.transform.position, -Root.transform.up * balanceHeight, Color.green);

                if (useStepPrediction)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(COMP.position, 0.3f);
                }
            }
        }

    }
}
