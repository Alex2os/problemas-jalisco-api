using System.Text.Json.Serialization;

namespace JaliscoProblemsAPI.Models;

public sealed record ReportCategory(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description
);

public static class ReportCategories
{
    private static readonly ReportCategory[] ActiveCategoryList =
    [
        new("baches", "Baches y vialidad", "Baches, pavimento dañado, problemas de calles o señalización vial."),
        new("alumbrado", "Alumbrado público", "Luminarias fundidas, postes dañados o fallas en el alumbrado público."),
        new("agua", "Agua y drenaje", "Fugas de agua potable, alcantarillas tapadas, drenaje o inundaciones."),
        new("basura", "Basura acumulada", "Basura en vía pública, acumulación de desechos o falta de recolección."),
        new("seguridad", "Seguridad urbana", "Falta de vigilancia, luminarias apagadas en zonas de riesgo o situaciones inseguras."),
        new("arboles", "Árboles y parques", "Árboles en riesgo de caída, ramas caídas o mantenimiento de parques."),
        new("semaforos", "Semáforos", "Semáforos apagados, dañados o con fallas de funcionamiento."),
        new("otro", "Otro problema", "Cualquier otra anomalía o problema urbano no contemplado en las categorías anteriores.")
    ];

    private static readonly HashSet<string> ActiveCategoryIds =
        new(ActiveCategoryList.Select(c => c.Id), StringComparer.Ordinal);

    // Identificadores históricos reconocidos para retrocompatibilidad
    private static readonly HashSet<string> LegacyCategoryIds = new(StringComparer.Ordinal)
    {
        "accidente",
        "bloqueo",
        "corte-de-servicios",
        "transporte-publico",
        "robo-asalto",
        "otros"
    };

    /// <summary>
    /// Catálogo público oficial de categorías activas para nuevos reportes.
    /// </summary>
    public static IReadOnlyList<ReportCategory> ActiveCategories => ActiveCategoryList;

    /// <summary>
    /// Determina si un identificador corresponde a una categoría activa vigente.
    /// </summary>
    public static bool IsActive(string? categoryId) =>
        categoryId is not null && ActiveCategoryIds.Contains(categoryId);

    /// <summary>
    /// Determina si un identificador es aceptado como entrada en creación de reportes (activo o alias histórico permitido 'otros').
    /// </summary>
    public static bool IsAllowedForCreation(string? categoryId) =>
        categoryId is not null && (ActiveCategoryIds.Contains(categoryId) || string.Equals(categoryId, "otros", StringComparison.Ordinal));

    /// <summary>
    /// Normaliza un identificador de categoría de entrada antes de su persistencia.
    /// Si es 'otros', se normaliza a 'otro'. Si es activa, se conserva.
    /// </summary>
    public static string Normalize(string categoryId) =>
        string.Equals(categoryId, "otros", StringComparison.Ordinal) ? "otro" : categoryId;

    /// <summary>
    /// Determina si un identificador es histórico reconocido.
    /// </summary>
    public static bool IsLegacy(string? categoryId) =>
        categoryId is not null && LegacyCategoryIds.Contains(categoryId);

    /// <summary>
    /// Determina si un identificador es reconocido por el sistema (activo o histórico).
    /// </summary>
    public static bool IsRecognized(string? categoryId) =>
        IsActive(categoryId) || IsLegacy(categoryId);
}

