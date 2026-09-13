namespace BusinessPartnerPortal.Api.Oracle;

internal static class PakistanFiscalWindow
{
    public static DateTime Start(DateTime? today = null)
    {
        var date = (today ?? DateTime.Today).Date;
        var fiscalYearStart =
            date.Month >= 7
                ? date.Year
                : date.Year - 1;

        return new DateTime(fiscalYearStart, 7, 1)
            .AddMonths(-3);
    }
}
