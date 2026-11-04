// ============================================================================
// WeaponPickup.cs — Recogida Específica de Armas 3D en el Mundo
// ============================================================================
// Especialización directa de WorldItemPickup para armas de fuego y combate:
// - Compatible con detección por contacto físico (OnTriggerEnter) e interacción con [E].
// - Registra y añade slots dinámicamente en InventoryUIController.
// - Suma munición a la reserva si el jugador ya posee el arma recogida.
// ============================================================================

using UnityEngine;

namespace DemenciaNoLimite.Pickups
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Demencia No Limite/Pickups/Weapon Pickup")]
    public class WeaponPickup : WorldItemPickup
    {
    }
}
