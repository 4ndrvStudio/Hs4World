using UnityEngine;


//-------------------------------------------------------------
//--APR Player
//--Camera Controller
//
//--Unity Asset Store - Version 1.0
//
//--By The Famous Mouse
//
//--Twitter @FamousMouse_Dev
//--Youtube TheFamouseMouse
//-------------------------------------------------------------
using HS4;

namespace ARP.APR.Scripts
{
    public class CameraController : MonoBehaviour
    {
        public static CameraController Instance;

        [Header("Player To Follow")]
        //Player root
        public Transform APRRoot;
    
        [Header("Follow Properties")]
        //Follow values
        public float distance = 10.0f; //The distance is only used when "rotateCamera" is enabled, when disabled the camera offset is used
        public float smoothness = 0.15f;
    
        [Header("Rotation Properties")]
        //Rotate with input
        public bool rotateCamera = true;
        public float rotateSpeed = 5.0f;
    
        //Min & max camera angle
        public float minAngle = -45.0f;
        public float maxAngle = -10.0f;
    
    
        //Private variables
        public Camera cam;
        private float currentX = 0.0f;
        private float currentY = 0.0f;
        private Quaternion rotation;
        private Vector3 dir;
        private Vector3 offset;


        //Lock and hide cursor
        void Start()
        {
            //Cursor.lockState = CursorLockMode.Locked;
            //Cursor.visible = false;
        
            //cam = Camera.main;
        
            offset = cam.transform.position;
            if (Instance == null)
                Instance = this;
        }


        //Camera mouse input and (clamping for rotation)
        void Update()
        {
            if (ControllerManager.Instance == null) return;
            currentX = currentX + ControllerManager.Instance.UITouchField.TouchDist.x * rotateSpeed;
            currentY = currentY + ControllerManager.Instance.UITouchField.TouchDist.y * rotateSpeed;

            currentY = Mathf.Clamp(currentY, minAngle, maxAngle);
        }

        public void SetFollow(Transform body)
        {
            APRRoot = body;
        }
    
    
        //Camera follow and rotation
        void FixedUpdate()
        {
            if(rotateCamera)
            {
                dir = new Vector3(0, 0, -distance);
                rotation = Quaternion.Euler(-currentY, currentX, 0);
                cam.transform.position = Vector3.Lerp (cam.transform.position, APRRoot.position + rotation * dir, smoothness);
                cam.transform.LookAt(APRRoot.position);
            }
        
            if(!rotateCamera)
            {
                var targetRotation = Quaternion.LookRotation(APRRoot.position - cam.transform.position);
                cam.transform.position = Vector3.Lerp (cam.transform.position, APRRoot.position + offset, smoothness);
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRotation, smoothness);
            }
        }

        

    }
}
