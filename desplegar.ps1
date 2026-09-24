Write-Host "Iniciando despliegue hacia 192.168.68.16..." -ForegroundColor Cyan
Write-Host "Atencion: Se te pedira la contrasena (632451) varias veces: una para copiar los archivos y otra para inicializar Docker." -ForegroundColor Yellow

# Crear directorio en remoto primero
Write-Host "`n[1/3] Preparando directorio en el servidor..." -ForegroundColor Cyan
ssh -o StrictHostKeyChecking=no localserver@192.168.68.16 "mkdir -p /home/localserver/inventario"

# Copiar todos los archivos, INCLUYENDO LOS CSV y la base de datos si ya se genero
Write-Host "`n[2/3] Copiando archivos y base de datos (archivos CSV)..." -ForegroundColor Cyan
scp -o StrictHostKeyChecking=no -r ./* localserver@192.168.68.16:/home/localserver/inventario/

# Generar certificados y desplegar docker
Write-Host "`n[3/3] Generando certificados HTTPS y levantando la aplicacion en Docker..." -ForegroundColor Cyan
ssh localserver@192.168.68.16 "cd /home/localserver/inventario && mkdir -p https && openssl req -x509 -newkey rsa:4096 -keyout https/key.pem -out https/cert.pem -sha256 -days 365 -nodes -subj '/CN=192.168.68.16' && openssl pkcs12 -export -out https/aspnetapp.pfx -inkey https/key.pem -in https/cert.pem -passout pass:Euridice123! && docker compose down && docker compose up -d --build"

Write-Host "`n===============================================" -ForegroundColor Green
Write-Host " DESPLIEGUE FINALIZADO EXITOSAMENTE" -ForegroundColor Green
Write-Host " Accede desde tu movil a: https://192.168.68.16" -ForegroundColor Green
Write-Host " (Acepta la advertencia de seguridad en el navegador)" -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Green
