# 🩸 DEMENCIA NO LÍMITE

> **Roguelike de terror psicológico y gore en primera persona (FPS)** ambientado en una ciudad fronteriza futurista y en ruinas entre **Brasil y Colombia**.

---

## 🏢 Estructura del Juego y Mecánicas Principales

### 1. La Torre (Generación Procedural de 27 Pisos)
El jugador (**João**) debe ascender una torre monolítica de 27 niveles para escapar de la frontera:
* **Pisos 1–8 (Acto 1 — Las Favelas Profundas):** Laberintos estrechos de concreto, barro y mutantes iniciales.
* **Piso 9 (Jefe 1):** ★ **El Carnicero** (*The Butcher*).
* **Pisos 10–17 (Acto 2 — La Podredumbre Industrial):** Fábricas inundadas de ácido, miasma tóxico y cultistas a distancia.
* **Piso 18 (Jefe 2):** ★ **La Máquina** (*The Machine*).
* **Pisos 19–26 (Acto 3 — El Ápex / Babel):** Arquitectura cibernética distorsionada y aberraciones de élite.
* **Piso 27 (Jefe Final):** ★ **Demencia Encarnada** (*Dementia Incarnate*).

---

### 2. AI Director (Dificultad Dinámica y Suerte)
Un sistema de IA subyacente que monitoriza en tiempo real:
* Daño recibido y eficiencia de bajas por minuto.
* Salud actual y estadísticas del jugador.
* Variable oculta de **Suerte** (`Luck`).
* Pondera proceduralmente el número de enemigos, la complejidad del laberinto, la densidad de trampas ambientales y la aparición de habitaciones secretas.

---

### 3. Jefes Adaptativos (Escalado Inverso)
Los jefes no tienen estadísticas fijas; se adaptan para castigar la build del jugador:
* **Si el jugador es Glass Cannon** (Alto Daño, Poca Vida Máxima): El jefe recibe una reserva de vida colosal y daño sostenido para forzar un combate largo.
* **Si el jugador es Tank** (Mucha Vida Máxima, Poco Daño): El jefe recibe poca vida pero ataques devastadores que ignoran el 85% de la defensa del jugador.
* **Fases y Modo Enfurecido:** Al descender de su umbral de vida, los jefes entran en estado de frenesí amplificando su velocidad y ferocidad.

---

### 4. Cabina del Elevador (Riesgo y Recompensa)
Entre cada piso, el jugador accede a un elevador seguro con uno de dos eventos aleatorios:
* **Mercader Clandestino:** Compra de suministros, mejoras y jeringas curativas a cambio de *Fichas*.
* **Pactos Malditos:** Elección de cartas de mutación donde João recibe un buff permanente (ej. +Velocidad, +Daño), pero todos los enemigos de la torre mutan para siempre (ej. capacidad de arrojar escombros letales, revivir, o explotar al morir).

---

### 5. Degradación Mental (Sanity System)
A medida que João asciende, la torre erosiona su mente a través de 5 estados:
* **Stable (0.0 – 0.2):** Cordura estable.
* **Uneasy (0.2 – 0.4):** Distorsiones de audio sutiles y taquicardia.
* **Disturbed (0.4 – 0.6):** Susurros psicóticos y viñeteado.
* **Fractured (0.6 – 0.8):** Aberración cromática severa, distorsión de lente y **aparición de enemigos fantasma** (alucinaciones).
* **Shattered (0.8 – 1.0):** Quiebre de la realidad, oscilación violenta del campo de visión (FOV) y decoloración a escala monocromática.

---

## 🎮 Controles

| Tecla / Acción | Función |
|---|---|
| **W, A, S, D** | Movimiento del personaje |
| **Mouse (Ratón)** | Apuntar y girar la cámara |
| **Clic Izquierdo** | Disparar arma / Ataque Melee |
| **Shift Izquierdo** | Correr (Sprint) |
| **Espacio** | Saltar |
| **R** | Recargar arma |
| **1, 2, 3 / Scroll** | Cambiar de arma |
| **Tab** | Liberar / Bloquear el cursor (para interactuar con botones de interfaz) |

---

## ⚡ Inicio Rápido en Unity

1. Abre el proyecto en **Unity 6 / 2022.3 LTS**.
2. En la barra superior de Unity, haz clic en:  
   👉 **`Demencia` > `⚡ Configurar Escena de Prueba (1 Click)`**
3. Presiona el botón de **Play (▶)**.
4. **Panel de Pruebas (Esquina superior derecha):** Usa el panel `🏢 EDIFICIO TORRE` para teletransportarte de inmediato al **Piso 01**, **Piso 09 (Jefe 1)**, **Piso 18 (Jefe 2)** o **Piso 27 (Jefe Final)**.

---

## 📁 Arquitectura del Código C#

```
Assets/Scripts/
├── Boss/           # Controladores de jefes adaptativos y arenas
├── Combat/         # Pipeline de daño, proyectiles, interfaces IDamageable
├── Core/           # GameManager, LevelGenerator, AIDirector, MentalDegradation, Elevator
│   ├── Data/       # PlayerStats, WeaponData, EnemyData, CursedPactData
│   └── Events/     # GameEvents (Bus estático de eventos fuertemente tipado)
├── Editor/         # Wizards de configuración en 1 clic
├── Enemies/        # IA de enemigos con mutadores de pactos y phantoms
├── Horror/         # Hooks de post-procesamiento, audio dinámico (BPM) y sacudida de pantalla
├── Level/          # LevelBuilder procedural, spawners de enemigos y elevador
├── Pickups/        # Fichas, medkits y munición con atracción magnética
├── Player/         # Controlador FPS, salud, inventario y manejo de armas
└── UI/             # HUD en pantalla, diálogos del elevador y barra de jefe
```
