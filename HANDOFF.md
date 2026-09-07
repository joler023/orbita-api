# HANDOFF

Documento de traspaso de contexto para quien retome este repo (otro desarrollador, u otra sesión de Claude Code). Objetivo: que puedas seguir trabajando sin releer todo el historial de commits. Se actualiza al terminar cada historia — no es una bitácora histórica, es una foto del momento.

Para las reglas de arquitectura/negocio vinculantes (que no cambian historia a historia), ver [`CLAUDE.md`](./CLAUDE.md). Este archivo es sobre *estado*, `CLAUDE.md` es sobre *reglas*.

## Última actualización

**2026-09-06** — tras terminar `ORB-A07` (invitar miembros al equipo), mergeado a `develop`.

## Qué está implementado

Ver la sección "Qué hay implementado hoy" en [`README.md`](./README.md) — se mantiene sincronizada ahí, no se duplica aquí.

Track A (Plataforma, Identidad y Facturación — dueño de este repo):

- [x] `ORB-A05` Registro de organización
- [x] `ORB-A09` Aislamiento entre organizaciones
- [x] `ORB-A06` Inicio y cierre de sesión
- [x] `ORB-A07` Invitar miembros al equipo
- [ ] `ORB-A08` Roles y permisos
- [ ] `ORB-A10` Recuperación de contraseña
- [ ] `ORB-A13` Medición de consumo
- [ ] Resto del track (historias que requieren cuentas de infraestructura en la nube, o marcadas P2 en el backlog) — sin empezar

## Decisiones que ya se tomaron (no reabrir sin motivo)

Estas están documentadas con más detalle en `CLAUDE.md`, se listan aquí para que salten a la vista antes de tocar el área relacionada:

- El JWT de acceso viaja en cookie `httpOnly`/`SameSite=Lax`, no en header `Authorization` — para que funcione igual con el handshake de SignalR más adelante.
- El JWT solo lleva `sub` (id de usuario), nunca un claim de tenant. Cuál organización actúa una sesión se resuelve explícitamente en cada endpoint tenant-scoped (ver `ORB-A07`), no se infiere del token.
- Las invitaciones toman el tenant de la ruta (`/api/tenants/{tenantId}/...`), no del JWT, para no necesitar nunca un "listar mis membresías cruzando tenants" que pelearía con Row-Level Security.
- El chequeo de "Owner o Admin" en `TeamInvitationService` es inline, a propósito — `ORB-A08` es la historia que lo generaliza a un framework de autorización real. No construir ese framework antes de tiempo.
- El envío de correo real está pendiente en toda la plataforma (no hay proveedor conectado); se loguea el link en su lugar (`LoggingInvitationEmailSender`). Cuando se construya `Notifications`, ese es el reemplazo, no un parche aquí.

## Cómo retomar el trabajo

1. Leer la tabla de arriba para saber qué falta.
2. `git fetch && git log --oneline develop..origin/develop` para confirmar que no hay nada mergeado que no se haya bajado.
3. Cada historia nueva es su propia rama `feature/<nombre>` desde `develop` (ver la convención de branching en `CLAUDE.md`). No continuar historias distintas en la misma rama.
4. El patrón de implementación (Domain → Application → Infrastructure → Api → Tests → Docs, cada capa su propio commit) está ya establecido en los commits de `ORB-A05` a `ORB-A07` — seguirlo en vez de inventar uno nuevo.
5. Antes de dar una historia por terminada: `dotnet build Orbita.slnx` sin errores, `dotnet test Orbita.slnx` en verde (requiere Docker corriendo para los tests de integración), y actualizar tanto `README.md` ("Qué hay implementado hoy") como este archivo.

## Riesgos y deuda conocida

- **Sin envío real de correo** (ver arriba) — bloquea probar el flujo de invitación con un buzón real, no solo con el log.
- **Autorización inline, no generalizada** — cada endpoint nuevo que necesite un chequeo de rol lo repite a mano hasta que exista `ORB-A08`. Vigilar que no se dupliquen variantes sutilmente distintas de esa lógica.
- **`orbita-front` sigue en scaffold** — no hay cliente HTTP ni pantallas reales todavía, así que ningún endpoint de este repo tiene todavía un consumidor real más allá de las pruebas de integración.
