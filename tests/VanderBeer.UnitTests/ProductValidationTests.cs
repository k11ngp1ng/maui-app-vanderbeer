using VanderBeer.Application.Catalog;
using VanderBeer.Contracts.Catalog;

namespace VanderBeer.UnitTests;

public sealed class ProductValidationTests
{
    private static ProductWriteRequest Valid() => new(Guid.NewGuid(), Guid.NewGuid(),
        "VB-TEST-1", "Cerveja teste", null, "Lata 350 ml", 10.00m, null, true, true);

    [Fact]
    public void Price_with_fraction_of_cent_is_rejected()
    {
        var errors = ProductValidation.Validate(Valid() with { Price = 10.001m });
        Assert.Contains(errors, x => x.Field == "price");
    }

    [Fact]
    public void Sku_must_arrive_uppercase_and_http_image_is_required()
    {
        var errors = ProductValidation.Validate(Valid() with { Sku = "vb-lower", ImageUrl = "file:///photo.png" });
        Assert.Contains(errors, x => x.Field == "sku");
        Assert.Contains(errors, x => x.Field == "imageUrl");
    }

    [Fact]
    public void Query_rejects_page_offset_that_cannot_fit_mysql_limit()
    {
        var errors = ProductValidation.ValidateQuery(new(int.MaxValue, 100, null, null, null));
        Assert.Contains(errors, x => x.Field == "page");
    }

    [Fact]
    public void Normalize_trims_display_text_without_changing_sku()
    {
        var normalized = ProductValidation.Normalize(Valid() with { Name = "  Café teste  ", Sku = "VB-TEST-1" });
        Assert.Equal("Café teste", normalized.Name);
        Assert.Equal("VB-TEST-1", normalized.Sku);
    }
}
