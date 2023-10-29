using System.Text.RegularExpressions;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;

namespace HS4
{
    public class NetworkStartUI : MonoBehaviour {

        [SerializeField] private GameObject _startGamePanel;
        [SerializeField] Button startClientButton;
        [SerializeField] Button startHostButton;
        [SerializeField] private string _serverIpAddress;
        [SerializeField] private GameObject _controller;


        void Start() {
           // Debug.Log("Starting Game...");
           startClientButton.onClick.AddListener(StartClient);
           startHostButton.onClick.AddListener(StartServer);
            Application.targetFrameRate = 60;

            if(Application.isBatchMode)
            {
                StartServer();
            }
           
        }
        void StartServer()
        {
            var utpTransport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            if (utpTransport)
            {
                utpTransport.SetConnectionData(Sanitize(_serverIpAddress), 7777);
            }
            NetworkManager.Singleton.StartServer();
            Debug.Log("Start Server");
        }
        void StartClient() {
            Debug.Log("Starting client");
            var utpTransport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            if (utpTransport)
            {
                utpTransport.SetConnectionData(Sanitize(_serverIpAddress), 7777);
            }
            NetworkManager.Singleton.StartClient();
            _controller.SetActive(true);
            Hide();
        }
        void StartHost()
        {

            Debug.Log("Starting Host");
            NetworkManager.Singleton.StartHost();
            Hide();
            _controller.SetActive(true);

        }

        void Hide() => _startGamePanel.SetActive(false);

        static string Sanitize(string dirtyString)
        {
            return Regex.Replace(dirtyString, "[^A-Za-z0-9.]", "");
        }
    }
}