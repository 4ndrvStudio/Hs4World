using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HS4
{
    public class ControllerManager : MonoBehaviour
    {
        public static ControllerManager Instance;

        public Joystick MovementJoy;
        public UITouchField UITouchField;

    // Start is called before the first frame update
    void Awake()
        {
            if (Instance == null)
                Instance = this;
        }
    }
}

