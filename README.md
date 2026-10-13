# Battery Test — Agente para Windows

Agente de código abierto para medir cuánto dura la batería de una laptop con Windows bajo una carga de trabajo simulada, y para registrar la salud de la batería.

## Estado

Prototipo de solo lectura. Muestra la batería (capacidad de diseño y actual, salud, ciclos, voltaje y potencia), el estado de energía, el uso de CPU en vivo y el hardware del equipo. **Todavía no genera carga ni envía ningún dato.**

## Compromisos de seguridad y privacidad

- La carga empieza solo cuando el usuario pulsa "Iniciar test". Nunca arranca sola ni en segundo plano.
- La ventana queda visible durante todo el test, con un botón de detener siempre a mano.
- No se inicia con Windows ni crea procesos ocultos.
- No envía números de serie, el usuario de Windows, el nombre del equipo ni nada que identifique a la persona.
- El usuario ve exactamente qué datos se envían antes de enviarlos.
- Los ejecutables se compilan solo en GitHub Actions, con attestation de procedencia y el SHA-256 publicado en cada release.

## Compilar y ejecutar

Requiere Windows y el SDK de .NET 9.

```powershell
dotnet run --project src/BatteryTest.Agent
```

## Estructura

- `src/BatteryTest.Core`: lectura de batería y hardware (WMI), estado de energía y contadores de CPU.
- `src/BatteryTest.Agent`: interfaz WPF.

## Licencia

[MIT](LICENSE)
