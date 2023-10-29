using Unity.Netcode.Components;
using UnityEngine;

namespace HS4 {
    public enum AuthorityMode {
        Server,
        Client
    }
    
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform {
        public AuthorityMode authorityMode = AuthorityMode.Client;

        protected override bool OnIsServerAuthoritative() => authorityMode == AuthorityMode.Server;
    }
}