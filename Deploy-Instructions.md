# Instrucciones de Despliegue - Farmacia Eurídice (Inventario)

## Requisitos Previos en el Servidor Local (192.168.68.16)
1. **Docker y Docker Compose** instalados.
2. Certificado HTTPS. Para usar la cámara del móvil (`Html5-QRCode`) es **estrictamente obligatorio** servir la aplicación sobre `https://` o `http://localhost`. Como accederás desde los teléfonos vía IP (ej. `https://192.168.68.16`), necesitas un certificado autofirmado en el servidor.

## Pasos para Despliegue

### 1. Copiar Archivos al Servidor
Abre PowerShell en tu PC y ejecuta los siguientes comandos para copiar el código al servidor (te pedirá la contraseña `632451`):

```powershell
scp -r ./* localserver@192.168.68.16:/home/localserver/inventario
```

*(Si no has puesto los archivos CSV, asegúrate de copiarlos también dentro de esa carpeta antes de construir la imagen).*

### 2. Conectarse vía SSH
```powershell
ssh localserver@192.168.68.16
```
*(Ingresa la contraseña `632451`)*

### 3. Generar Certificado HTTPS Autofirmado (En el servidor)
Dentro del servidor, genera un certificado para .NET:
```bash
cd /home/localserver/inventario
mkdir -p https
dotnet dev-certs https -ep ./https/aspnetapp.pfx -p "Euridice123!"
```
*(Si el servidor no tiene el SDK de .NET instalado para generar el certificado, puedes usar OpenSSL).*

Con OpenSSL:
```bash
mkdir -p https
openssl req -x509 -newkey rsa:4096 -keyout https/key.pem -out https/cert.pem -sha256 -days 365 -nodes -subj "/CN=192.168.68.16"
openssl pkcs12 -export -out https/aspnetapp.pfx -inkey https/key.pem -in https/cert.pem -passout pass:Euridice123!
```

### 4. Construir y Ejecutar el Contenedor Docker
Crea un archivo `docker-compose.yml` en el servidor (o usa el que te he provisto) y levanta el servicio:

```bash
docker compose up -d --build
```

### 5. Acceso desde Dispositivos Móviles
Abre el navegador en el teléfono (Chrome/Safari) e ingresa a:
👉 `https://192.168.68.16`

**Nota:** Como es un certificado autofirmado, el navegador mostrará una advertencia de seguridad ("La conexión no es privada"). Debes darle a "Avanzado" -> "Continuar a 192.168.68.16 (inseguro)". A partir de ahí, la cámara funcionará perfectamente.
