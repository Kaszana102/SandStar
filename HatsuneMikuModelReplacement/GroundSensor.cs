
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SandStarEnemy
{
    class GroundSensor : MonoBehaviour
    {
        public SandStarAI star;

        //check if wall was hit
        private void OnTriggerEnter(Collider other)
        {            
            // for some it's terrain XD            
            if (other.gameObject.layer == LayerMask.NameToLayer("Room"))
            {
                star.StopAttacking();
            }
        }
    }
}
