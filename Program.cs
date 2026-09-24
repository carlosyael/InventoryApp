using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var dbPath = Environment.GetEnvironmentVariable("DB_DIR") != null 
    ? Path.Combine(Environment.GetEnvironmentVariable("DB_DIR")!, "inventario_euridice.db") 
    : "inventario_euridice.db";

builder.Services.AddDbContext<InventarioDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseStaticFiles();

// Ensure DB is created and Seeded
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<InventarioDbContext>();
    dbContext.Database.EnsureCreated();
    await DatabaseSeeder.SeedAsync(dbContext);
}

// ======================== ENDPOINTS ========================

app.MapGet("/api/productos", async (InventarioDbContext db) =>
{
    return Results.Ok(await db.Productos.ToListAsync());
});

app.MapGet("/api/productos/version", async (InventarioDbContext db) =>
{
    var count = await db.Productos.CountAsync();
    return Results.Ok(new 
    { 
        version = $"{count}_{CatalogoState.Version}",
        totalProductos = count
    });
});

app.MapPost("/api/inventario/sync", async (InventarioDbContext db, List<ConteoFisico> conteos) =>
{
    int saved = 0;
    foreach (var conteo in conteos)
    {
        bool exists = await db.Conteos.AnyAsync(c => c.Id == conteo.Id);
        if (!exists)
        {
            db.Conteos.Add(conteo);
            saved++;
        }
    }
    if (saved > 0) await db.SaveChangesAsync();
    return Results.Ok(new { Guardados = saved });
});

app.MapGet("/api/inventario/contados", async (InventarioDbContext db) =>
{
    var contados = await db.Conteos
        .Select(c => new { 
            c.Id, 
            c.ProductoCodigo, 
            c.CantidadFisica, 
            c.Ubicacion, 
            c.LoteContado, 
            c.VencimientoContado, 
            c.Usuario, 
            c.FechaRegistro 
        })
        .ToListAsync();
    return Results.Ok(contados);
});

app.MapPost("/api/inventario/eliminar-conteo", async (EliminarConteoRequest req, InventarioDbContext db) =>
{
    var conteo = await db.Conteos.FirstOrDefaultAsync(c => c.Id == req.Id);
    if (conteo == null) return Results.NotFound(new { mensaje = "Toma no encontrada en el servidor." });
    db.Conteos.Remove(conteo);
    await db.SaveChangesAsync();
    return Results.Ok(new { mensaje = "Toma eliminada exitosamente", id = req.Id });
}).DisableAntiforgery();

app.MapDelete("/api/inventario/conteo/{id:guid}", async (Guid id, InventarioDbContext db) =>
{
    var conteo = await db.Conteos.FirstOrDefaultAsync(c => c.Id == id);
    if (conteo == null) return Results.NotFound(new { mensaje = "Toma no encontrada en el servidor." });
    db.Conteos.Remove(conteo);
    await db.SaveChangesAsync();
    return Results.Ok(new { mensaje = "Toma eliminada exitosamente", id });
}).DisableAntiforgery();

app.MapPost("/api/inventario/editar-conteo", async (EditarConteoRequest req, InventarioDbContext db) =>
{
    var conteo = await db.Conteos.FirstOrDefaultAsync(c => c.Id == req.Id);
    if (conteo == null) return Results.NotFound(new { mensaje = "Toma no encontrada en el servidor." });
    
    if (req.CantidadFisica < 0) return Results.BadRequest(new { mensaje = "La cantidad física no puede ser negativa." });
    conteo.CantidadFisica = req.CantidadFisica;
    if (req.Lote != null) conteo.LoteContado = req.Lote.Trim();
    if (req.Vencimiento != null) conteo.VencimientoContado = req.Vencimiento.Trim();
    if (req.Ubicacion != null) conteo.Ubicacion = req.Ubicacion.Trim();
    
    await db.SaveChangesAsync();
    return Results.Ok(new { mensaje = "Toma actualizada exitosamente", conteo });
}).DisableAntiforgery();

app.MapGet("/api/inventario/exportar-conciliacion", async (InventarioDbContext db) =>
{
    var allConteos = await db.Conteos.ToListAsync();
    var productos = await db.Productos.ToDictionaryAsync(p => p.Codigo);

    var consolidado = allConteos
        .GroupBy(c => c.ProductoCodigo)
        .Select(g =>
        {
            var codigo = g.Key;
            productos.TryGetValue(codigo, out var p);
            return new
            {
                Codigo = codigo,
                CodigoDeBarras = p?.CodigoBarra ?? "",
                Descripcion = p?.Descripcion ?? "",
                Unidad = p?.Unidad ?? "",
                Costo = p?.Costo ?? 0m,
                Precio = p?.Precio ?? 0m,
                TotalFisicoContado = g.Sum(x => x.CantidadFisica),
                Lotes = string.Join(" | ", g.Select(x => $"{x.LoteContado} ({x.VencimientoContado})").Where(s => s != " ()").Distinct()),
                TomasTotales = g.Count()
            };
        });

    return Results.Ok(consolidado);
});

app.MapGet("/api/inventario/exportar-csv", async (InventarioDbContext db) =>
{
    var allConteos = await db.Conteos.ToListAsync();
    var productos = await db.Productos.ToDictionaryAsync(p => p.Codigo);

    var consolidado = allConteos
        .GroupBy(c => c.ProductoCodigo)
        .Select(g =>
        {
            var codigo = g.Key;
            productos.TryGetValue(codigo, out var p);
            return new
            {
                Codigo = codigo,
                CodigoBarra = p?.CodigoBarra ?? "",
                Descripcion = p?.Descripcion ?? "",
                Unidad = p?.Unidad ?? "",
                Costo = p?.Costo ?? 0m,
                Precio = p?.Precio ?? 0m,
                TotalFisicoContado = g.Sum(x => x.CantidadFisica),
                Lotes = string.Join(" | ", g.Select(x => $"{x.LoteContado} ({x.VencimientoContado})").Where(s => s != " ()").Distinct()),
                TomasTotales = g.Count()
            };
        });

    var sb = new StringBuilder();
    sb.AppendLine("Codigo;CodigoDeBarras;Descripcion;Unidad;Costo;Precio;TotalFisicoContado;Lotes;TomasTotales");
    foreach (var c in consolidado)
    {
        sb.AppendLine($"\"{c.Codigo}\";\"{c.CodigoBarra}\";\"{c.Descripcion}\";\"{c.Unidad}\";\"{c.Costo}\";\"{c.Precio}\";\"{c.TotalFisicoContado}\";\"{c.Lotes}\";\"{c.TomasTotales}\"");
    }

    return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"Conciliacion_Euridice_{DateTime.Now:yyyy-MM-dd}.csv");
});

// Productos pendientes de contar (no aparecen en Conteos)
app.MapGet("/api/inventario/pendientes-contar", async (InventarioDbContext db) =>
{
    var codigosContados = await db.Conteos
        .Select(c => c.ProductoCodigo)
        .Distinct()
        .ToListAsync();

    var pendientes = await db.Productos
        .Where(p => !codigosContados.Contains(p.Codigo))
        .Select(p => new { p.Codigo, p.CodigoBarra, p.Descripcion, p.Unidad })
        .ToListAsync();

    return Results.Ok(new { total = pendientes.Count, productos = pendientes });
});

// Resetear todos los conteos físicos (mantiene productos intactos)
app.MapPost("/api/inventario/resetear-conteos", async (InventarioDbContext db) =>
{
    var count = await db.Conteos.CountAsync();
    db.Conteos.RemoveRange(db.Conteos);
    await db.SaveChangesAsync();
    return Results.Ok(new { eliminados = count, mensaje = $"Se han reseteado {count} registros de conteos físicos. El catálogo se mantiene intacto." });
}).DisableAntiforgery();

// Actualizar o asignar código de barra a un producto existente
app.MapPost("/api/productos/actualizar-barcode", async (ActualizarBarcodeRequest req, InventarioDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Codigo))
        return Results.BadRequest("El código de producto es requerido");

    var prod = await db.Productos.FirstOrDefaultAsync(p => p.Codigo == req.Codigo.Trim());
    if (prod == null)
        return Results.NotFound("Producto no encontrado");

    var nuevoBarcode = (req.CodigoBarra ?? string.Empty).Trim();
    prod.CodigoBarra = nuevoBarcode;
    await db.SaveChangesAsync();
    CatalogoState.NotifyCatalogChanged();

    return Results.Ok(new 
    { 
        mensaje = $"Código de barra actualizado exitosamente para {prod.Descripcion}", 
        codigo = prod.Codigo, 
        codigoBarra = prod.CodigoBarra 
    });
}).DisableAntiforgery();

// Cargar CSV en caliente
app.MapPost("/api/productos/cargar-csv", async (HttpRequest request, InventarioDbContext db) =>
{
    if (!request.HasFormContentType) return Results.BadRequest("Se requiere multipart/form-data");

    var form = await request.ReadFormAsync();
    var fileProd = form.Files.GetFile("productos");
    var fileExist = form.Files.GetFile("existencias");

    if (fileProd == null) return Results.BadRequest("Se requiere el archivo 'productos' (Productos_Migrados.csv)");

    db.Productos.RemoveRange(db.Productos);
    await db.SaveChangesAsync();

    var productos = new Dictionary<string, ProductoCatalogo>();

    using (var reader = new StreamReader(fileProd.OpenReadStream(), Encoding.Latin1))
    {
        string? line;
        bool isFirst = true;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (isFirst) { isFirst = false; continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = line.Split(';').Select(x => x.Trim('"')).ToArray();
            if (cols.Length > 20 && !string.IsNullOrWhiteSpace(cols[0]))
            {
                var codigo = cols[0].Trim();
                decimal.TryParse(cols[17], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal costo);
                decimal.TryParse(cols[16], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal precio);
                productos[codigo] = new ProductoCatalogo
                {
                    Codigo = codigo,
                    Descripcion = cols[1],
                    CodigoOld = cols[3],
                    Unidad = cols[15],
                    Precio = precio,
                    Costo = costo,
                    CodigoBarra = cols[20]
                };
            }
        }
    }

    if (fileExist != null)
    {
        using var reader = new StreamReader(fileExist.OpenReadStream(), Encoding.Latin1);
        string? line;
        bool isFirst = true;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (isFirst) { isFirst = false; continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = line.Split(';').Select(x => x.Trim('"')).ToArray();
            if (cols.Length >= 5 && !string.IsNullOrWhiteSpace(cols[0]))
            {
                var codigo = cols[0].Trim();
                if (productos.TryGetValue(codigo, out var prod))
                {
                    prod.LoteReferencia = cols[4];
                }
            }
        }
    }

    if (productos.Any())
    {
        await db.Productos.AddRangeAsync(productos.Values);
        await db.SaveChangesAsync();
        CatalogoState.NotifyCatalogChanged();
    }

    return Results.Ok(new { Cargados = productos.Count, Mensaje = $"Catálogo actualizado con {productos.Count} productos." });
}).DisableAntiforgery();

// Estadísticas
app.MapGet("/api/stats", async (InventarioDbContext db) =>
{
    var totalProductos = await db.Productos.CountAsync();
    var totalConteos = await db.Conteos.CountAsync();
    var codigosContados = await db.Conteos.Select(c => c.ProductoCodigo).Distinct().ToListAsync();
    return Results.Ok(new { totalProductos, totalConteos, productosContados = codigosContados.Count });
});

app.MapFallbackToFile("index.html");

app.Run();

// ======================== MODELS ========================

public class ProductoCatalogo
{
    public string Codigo { get; set; } = string.Empty;
    public string CodigoBarra { get; set; } = string.Empty;
    public string CodigoOld { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Unidad { get; set; } = "UNIDAD";
    public decimal Precio { get; set; }
    public decimal Costo { get; set; }
    public string LoteReferencia { get; set; } = string.Empty;
}

public class ActualizarBarcodeRequest
{
    public string Codigo { get; set; } = string.Empty;
    public string CodigoBarra { get; set; } = string.Empty;
}

public class EliminarConteoRequest
{
    public Guid Id { get; set; }
}

public class EditarConteoRequest
{
    public Guid Id { get; set; }
    public decimal CantidadFisica { get; set; }
    public string? Lote { get; set; }
    public string? Vencimiento { get; set; }
    public string? Ubicacion { get; set; }
}

public class ConteoFisico
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProductoCodigo { get; set; } = string.Empty;
    public string CodigoBarraLeido { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal CantidadFisica { get; set; }
    public string ModoOperacion { get; set; } = "nuevo";
    public string LoteContado { get; set; } = string.Empty;
    public string VencimientoContado { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = "Piso de Venta";
    public string Usuario { get; set; } = "Operador";
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
}

public class InventarioDbContext : DbContext
{
    public InventarioDbContext(DbContextOptions<InventarioDbContext> options) : base(options) { }

    public DbSet<ProductoCatalogo> Productos => Set<ProductoCatalogo>();
    public DbSet<ConteoFisico> Conteos => Set<ConteoFisico>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductoCatalogo>().HasKey(p => p.Codigo);
        modelBuilder.Entity<ConteoFisico>().HasKey(c => c.Id);
    }
}

public static class DatabaseSeeder
{
    public static async Task SeedAsync(InventarioDbContext dbContext)
    {
        if (await dbContext.Productos.AnyAsync())
            return;

        var productos = new Dictionary<string, ProductoCatalogo>();

        string prodFile = "Productos_Migrados.csv";
        string existFile = "ExistenciaAlmacen_Migrado.csv";

        if (File.Exists(prodFile))
        {
            var lines = await File.ReadAllLinesAsync(prodFile, Encoding.Latin1);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = lines[i].Split(';').Select(x => x.Trim('"')).ToArray();
                if (cols.Length > 20 && !string.IsNullOrWhiteSpace(cols[0]))
                {
                    var codigo = cols[0].Trim();
                    decimal.TryParse(cols[17], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal costo);
                    decimal.TryParse(cols[16], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal precio);
                    productos[codigo] = new ProductoCatalogo
                    {
                        Codigo = codigo,
                        Descripcion = cols[1],
                        CodigoOld = cols[3],
                        Unidad = cols[15],
                        Precio = precio,
                        Costo = costo,
                        CodigoBarra = cols[20]
                    };
                }
            }
        }

        if (File.Exists(existFile))
        {
            var lines = await File.ReadAllLinesAsync(existFile, Encoding.Latin1);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = lines[i].Split(';').Select(x => x.Trim('"')).ToArray();
                if (cols.Length >= 5 && !string.IsNullOrWhiteSpace(cols[0]))
                {
                    var codigo = cols[0].Trim();
                    if (productos.TryGetValue(codigo, out var prod))
                    {
                        prod.LoteReferencia = cols[4];
                    }
                }
            }
        }

        if (productos.Any())
        {
            await dbContext.Productos.AddRangeAsync(productos.Values);
            await dbContext.SaveChangesAsync();
        }
    }
}

public static class CatalogoState
{
    public static string Version { get; set; } = DateTime.UtcNow.Ticks.ToString();
    public static void NotifyCatalogChanged() => Version = DateTime.UtcNow.Ticks.ToString();
}
