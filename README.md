# Battery Test — Agente para Windows

Agente de código abierto para medir cuánto dura la batería de una laptop con Windows bajo una carga de trabajo simulada, y para registrar la salud de la batería.

## Estado

Versión 0.1 (en desarrollo):

- Lectura de batería (capacidad de diseño y actual, salud, ciclos, voltaje y potencia), estado de energía, plan de energía, uso de CPU y hardware.
- Test de batería con perfiles de carga **ligero, medio, alto, extremo** y **personalizado** (CPU, memoria y un poco de disco). La carga de GPU y de video todavía no está implementada.
- Registro local de cada test y recuperación del test si la laptop se apaga al agotarse la batería.
- **Todavía no envía ningún dato.**

## Cómo funciona el test

1. Eliges un perfil y confirmas la lista de verificación (programas cerrados, Wi-Fi conectado, Bluetooth apagado, sin periféricos USB).
2. Con el cargador desconectado, pulsas «Iniciar test». Nada empieza antes.
3. El agente genera la carga en ráfagas y reposos, y guarda una lectura cada 30 segundos.
4. El test termina cuando lo detienes, cuando conectas el cargador o cuando la batería llega al mínimo que configuraste. Si la laptop se apaga antes, el test se recupera la próxima vez que abras el agente.

El consumo y la duración se calculan con la caída de la carga restante (mWh) en el tiempo, no con la tasa de descarga instantánea.

## Cambios temporales al sistema durante el test

- **Brillo:** se fija la pantalla interna al 50 % y al terminar se restaura tu valor. Si el equipo se apaga a mitad de test, se restaura al abrir el agente de nuevo.
- **Suspensión:** el equipo no se suspende ni apaga la pantalla mientras dura el test. Al terminar, Windows vuelve a su configuración normal.
- **Disco:** se escribe un archivo temporal de hasta 64 MB en `%TEMP%\BatteryTest`, que se borra al terminar.

El agente no se inicia con Windows, no deja procesos en segundo plano y solo puede haber uno abierto a la vez.

## Dónde se guardan los resultados

En `%LOCALAPPDATA%\BatteryTest\tests`, un archivo `.jsonl` por test (una lectura por línea).

## Compromisos de seguridad y privacidad

- La carga empieza solo cuando el usuario pulsa «Iniciar test». Nunca arranca sola ni en segundo plano.
- La ventana queda visible durante todo el test, con un botón de detener siempre a mano.
- No se envían números de serie, el usuario de Windows, el nombre del equipo ni nada que identifique a la persona.
- El usuario verá exactamente qué datos se envían antes de enviarlos.
- Los ejecutables de cada versión se compilarán solo en GitHub Actions, con attestation de procedencia y el SHA-256 publicado.

## Compilar y ejecutar

Requiere Windows y el SDK de .NET 9 (`winget install Microsoft.DotNet.SDK.9`).

```powershell
git clone https://github.com/Josue1210/battery-test-agent
cd battery-test-agent
dotnet run --project src/BatteryTest.Agent
```

Pruebas automáticas:

```powershell
dotnet test
```

## Estructura

- `src/BatteryTest.Core`: lectura de batería y hardware (WMI), condiciones del test, generación de carga y registro.
- `src/BatteryTest.Agent`: interfaz WPF.
- `tests/BatteryTest.Core.Tests`: pruebas del registro y de los cálculos.

## Licencia

[MIT](LICENSE)
