using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HS4
{
    [CreateAssetMenu(fileName ="InputReader", menuName ="4ndrv/Input Reader")]
    public class InputReader : ScriptableObject, PlayerInputActions.IPlayerActions
    {
        public Vector3 Move => inputActions.Player.Move.ReadValue<Vector2>();
        public bool IsLeftPunch => inputActions.Player.LeftPunch.ReadValue<float>() > 0;
        public bool IsRightPunch => inputActions.Player.RightPunch.ReadValue<float>() > 0;
        public float LeftReach => inputActions.Player.ReachLeft.ReadValue<float>();
        public float RightReach => inputActions.Player.ReachRight.ReadValue<float>();
        public float Jump => inputActions.Player.Jump.ReadValue<float>();

        PlayerInputActions inputActions;

        private void OnEnable()
        {
            if (inputActions == null)
            {
                inputActions = new PlayerInputActions();
                inputActions.Player.SetCallbacks(this);
            }
        }

        public void Enable()
        {
            inputActions.Enable();
        }

        public void OnBrake(InputAction.CallbackContext context)
        {
            //no
        }

        public void OnFire(InputAction.CallbackContext context)
        {
            //no
        }

        public void OnLook(InputAction.CallbackContext context)
        {
            //no
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            //no
        }

        public void OnLeftPunch(InputAction.CallbackContext context)
        {
            //throw new System.NotImplementedException();
        }

        public void OnRightPunch(InputAction.CallbackContext context)
        {
           // throw new System.NotImplementedException();
        }

     

        public void OnJump(InputAction.CallbackContext context)
        {
            //throw new System.NotImplementedException();
        }

        public void OnReachLeft(InputAction.CallbackContext context)
        {
           // throw new System.NotImplementedException();
        }

        public void OnReachRight(InputAction.CallbackContext context)
        {
           // throw new System.NotImplementedException();
        }
    }
}
