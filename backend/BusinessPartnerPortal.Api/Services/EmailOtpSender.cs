using System.Net;
using System.Net.Mail;

namespace BusinessPartnerPortal.Api.Services;

public sealed class EmailOtpSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailOtpSender> _logger;

    public EmailOtpSender(
        IConfiguration config,
        ILogger<EmailOtpSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ============================================================
    // REGISTRATION OTP EMAIL
    // ============================================================

    public async Task SendAsync(
        string email,
        string otp,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException(
                "Recipient email address is required.",
                nameof(email));
        }

        if (string.IsNullOrWhiteSpace(otp))
        {
            throw new ArgumentException(
                "OTP is required.",
                nameof(otp));
        }

        var subject =
            "Pakistan Cables Portal Verification Code";

        var safeOtp =
            WebUtility.HtmlEncode(otp);

        var body = $"""
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width">
    <title>Verification Code</title>
</head>

<body style="
    margin:0;
    padding:0;
    background-color:#f4f7f5;
    font-family:Arial,Helvetica,sans-serif;
">

<table
    role="presentation"
    width="100%"
    cellspacing="0"
    cellpadding="0"
    border="0"
    style="
        width:100%;
        background-color:#f4f7f5;
    "
>
    <tr>
        <td
            align="center"
            style="
                padding:32px 15px;
            "
        >

            <table
                role="presentation"
                width="600"
                cellspacing="0"
                cellpadding="0"
                border="0"
                style="
                    width:600px;
                    max-width:600px;
                    background-color:#ffffff;
                    border:1px solid #e1e7e4;
                "
            >

                <tr>
                    <td
                        style="
                            height:6px;
                            background-color:#079447;
                            font-size:0;
                            line-height:0;
                        "
                    >
                        &nbsp;
                    </td>
                </tr>

                <tr>
                    <td
                        style="
                            padding:34px 36px;
                            color:#172033;
                        "
                    >

                        <table
                            role="presentation"
                            width="100%"
                            cellspacing="0"
                            cellpadding="0"
                            border="0"
                        >

                            <tr>
                                <td
                                    style="
                                        padding-bottom:18px;
                                        font-family:Arial,Helvetica,sans-serif;
                                        font-size:24px;
                                        line-height:30px;
                                        font-weight:bold;
                                        color:#079447;
                                    "
                                >
                                    Pakistan Cables Business Partner's Portal
                                </td>
                            </tr>

                            <tr>
                                <td
                                    style="
                                        padding-bottom:20px;
                                        font-family:Arial,Helvetica,sans-serif;
                                        font-size:15px;
                                        line-height:24px;
                                        color:#344054;
                                    "
                                >
                                    Your verification code is:
                                </td>
                            </tr>

                            <tr>
                                <td
                                    align="center"
                                    style="
                                        padding-bottom:24px;
                                    "
                                >
                                    <table
                                        role="presentation"
                                        cellspacing="0"
                                        cellpadding="0"
                                        border="0"
                                        align="center"
                                    >
                                        <tr>
                                            <td
                                                align="center"
                                                style="
                                                    padding:18px 28px;
                                                    background-color:#eef8f2;
                                                    border:1px solid #c7e7d4;
                                                    font-family:Arial,Helvetica,sans-serif;
                                                    font-size:30px;
                                                    line-height:34px;
                                                    font-weight:bold;
                                                    letter-spacing:8px;
                                                    color:#078c45;
                                                "
                                            >
                                                {safeOtp}
                                            </td>
                                        </tr>
                                    </table>
                                </td>
                            </tr>

                            <tr>
                                <td
                                    style="
                                        padding-bottom:24px;
                                        font-family:Arial,Helvetica,sans-serif;
                                        font-size:13px;
                                        line-height:20px;
                                        color:#667085;
                                    "
                                >
                                    If you did not request this verification code,
                                    please ignore this email.
                                </td>
                            </tr>

                            <tr>
                                <td
                                    style="
                                        font-family:Arial,Helvetica,sans-serif;
                                        font-size:14px;
                                        line-height:20px;
                                        color:#172033;
                                    "
                                >
                                    Regards,<br>
                                    <strong>Pakistan Cables Limited</strong><br>
                                    Business Partner's Portal
                                </td>
                            </tr>

                        </table>

                    </td>
                </tr>

            </table>

        </td>
    </tr>
</table>

</body>
</html>
""";

        await SendEmailAsync(
            email,
            subject,
            body,
            ct);
    }

    // ============================================================
    // VENDOR PORTAL ACCESS / PASSWORD SETUP EMAIL
    // ============================================================

    public async Task SendPortalAccessAsync(
        string email,
        string vendorName,
        string setupUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException(
                "Vendor email address is required.",
                nameof(email));
        }

        if (string.IsNullOrWhiteSpace(setupUrl))
        {
            throw new ArgumentException(
                "Password setup URL is required.",
                nameof(setupUrl));
        }

        var safeEmail =
            WebUtility.HtmlEncode(email);

        var safeVendorName =
            WebUtility.HtmlEncode(
                string.IsNullOrWhiteSpace(vendorName)
                    ? "Vendor"
                    : vendorName);

        var safeSetupUrl =
            WebUtility.HtmlEncode(setupUrl);

        var subject =
            "Pakistan Cables Business Partner's Portal - Set Your Password";

        var body = $"""
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width">
    <title>Set Your Password</title>
</head>

<body style="
    margin:0;
    padding:0;
    background-color:#f4f7f5;
    font-family:Arial,Helvetica,sans-serif;
">

<table
    role="presentation"
    width="100%"
    cellspacing="0"
    cellpadding="0"
    border="0"
    style="
        width:100%;
        background-color:#f4f7f5;
    "
>
    <tr>
        <td
            align="center"
            style="
                padding:32px 15px;
            "
        >

            <table
                role="presentation"
                width="620"
                cellspacing="0"
                cellpadding="0"
                border="0"
                style="
                    width:620px;
                    max-width:620px;
                    background-color:#ffffff;
                    border:1px solid #e1e7e4;
                "
            >

                <!-- TOP GREEN LINE -->
                <tr>
                    <td
                        style="
                            height:6px;
                            background-color:#079447;
                            font-size:0;
                            line-height:0;
                        "
                    >
                        &nbsp;
                    </td>
                </tr>

                <!-- MAIN CONTENT -->
                <tr>
                    <td
                        style="
                            padding:36px 40px 34px 40px;
                            color:#172033;
                        "
                    >

                        <table
                            role="presentation"
                            width="100%"
                            cellspacing="0"
                            cellpadding="0"
                            border="0"
                        >

                            <tr>
                                <td
                                    style="
                                        padding-bottom:18px;
                                        font-family:Arial,Helvetica,sans-serif;
                                        font-size:16px;
                                        line-height:24px;
                                        color:#172033;
                                    "
                                >
                                    Dear <strong>{safeVendorName}</strong>,
                                </td>
                            </tr>

                            <tr>
                                <td
                                    style="
                                        padding-bottom:22px;
                                        font-family:Arial,Helvetica,sans-serif;
                                        font-size:28px;
                                        line-height:36px;
                                        font-weight:bold;
                                        color:#079447;
                                    "
                                >
                                    Welcome to Pakistan Cables<br>
                                    Business Partner's Portal
                                </td>
                            </tr>

                            <tr>
                                <td
                                    style="
                                        padding-bottom:16px;
                                        font-family:Arial,Helvetica,sans-serif;
                                        font-size:15px;
                                        line-height:24px;
                                        color:#344054;
                                    "
                                >
                                    We are pleased to inform you that portal access has
                                    been enabled for your registered vendor account with
                                    Pakistan Cables.
                                </td>
                            </tr>

                            <tr>
                                <td
                                    style="
                                        padding-bottom:28px;
                                        font-family:Arial,Helvetica,sans-serif;
                                        font-size:15px;
                                        line-height:24px;
                                        color:#344054;
                                    "
                                >
                                    Please click the button below to create your password
                                    and activate your portal login.
                                </td>
                            </tr>

                            <!-- OUTLOOK SAFE BUTTON -->
                            <tr>
                                <td
                                    align="center"
                                    style="
                                        padding-bottom:30px;
                                    "
                                >

                                    <!--[if mso]>
                                    <v:roundrect
                                        xmlns:v="urn:schemas-microsoft-com:vml"
                                        xmlns:w="urn:schemas-microsoft-com:office:word"
                                        href="{safeSetupUrl}"
                                        style="
                                            height:48px;
                                            v-text-anchor:middle;
                                            width:260px;
                                        "
                                        arcsize="10%"
                                        strokecolor="#079447"
                                        fillcolor="#079447"
                                    >
                                        <w:anchorlock/>

                                        <center
                                            style="
                                                color:#ffffff;
                                                font-family:Arial,sans-serif;
                                                font-size:16px;
                                                font-weight:bold;
                                            "
                                        >
                                            Set Your Password
                                        </center>
                                    </v:roundrect>
                                    <![endif]-->

                                    <!--[if !mso]><!-- -->
                                    <table
                                        role="presentation"
                                        cellspacing="0"
                                        cellpadding="0"
                                        border="0"
                                        align="center"
                                    >
                                        <tr>
                                            <td
                                                align="center"
                                                bgcolor="#079447"
                                                style="
                                                    background-color:#079447;
                                                    border-radius:6px;
                                                "
                                            >
                                                <a
                                                    href="{safeSetupUrl}"
                                                    target="_blank"
                                                    style="
                                                        display:inline-block;
                                                        width:260px;
                                                        padding:14px 0;
                                                        background-color:#079447;
                                                        color:#ffffff !important;
                                                        text-decoration:none !important;
                                                        font-family:Arial,Helvetica,sans-serif;
                                                        font-size:16px;
                                                        line-height:20px;
                                                        font-weight:bold;
                                                        text-align:center;
                                                        border-radius:6px;
                                                    "
                                                >
                                                    <span
                                                        style="
                                                            color:#ffffff !important;
                                                            text-decoration:none !important;
                                                        "
                                                    >
                                                        Set Your Password
                                                    </span>
                                                </a>
                                            </td>
                                        </tr>
                                    </table>
                                    <!--<![endif]-->

                                </td>
                            </tr>

                            <!-- LOGIN DETAILS -->
                            <tr>
                                <td
                                    style="
                                        padding-bottom:26px;
                                    "
                                >

                                    <table
                                        role="presentation"
                                        width="100%"
                                        cellspacing="0"
                                        cellpadding="0"
                                        border="0"
                                        style="
                                            width:100%;
                                            background-color:#f0faf4;
                                            border:1px solid #bfe6cf;
                                        "
                                    >

                                        <tr>
                                            <td
                                                style="
                                                    padding:20px 22px;
                                                "
                                            >

                                                <table
                                                    role="presentation"
                                                    width="100%"
                                                    cellspacing="0"
                                                    cellpadding="0"
                                                    border="0"
                                                >

                                                    <tr>
                                                        <td
                                                            width="145"
                                                            valign="top"
                                                            style="
                                                                padding:7px 0;
                                                                font-family:Arial,Helvetica,sans-serif;
                                                                font-size:14px;
                                                                line-height:20px;
                                                                color:#667085;
                                                            "
                                                        >
                                                            Email Address
                                                        </td>

                                                        <td
                                                            valign="top"
                                                            style="
                                                                padding:7px 0;
                                                                font-family:Arial,Helvetica,sans-serif;
                                                                font-size:14px;
                                                                line-height:20px;
                                                                font-weight:bold;
                                                                color:#172033;
                                                            "
                                                        >
                                                            {safeEmail}
                                                        </td>
                                                    </tr>

                                                    <tr>
                                                        <td
                                                            width="145"
                                                            valign="top"
                                                            style="
                                                                padding:7px 0;
                                                                font-family:Arial,Helvetica,sans-serif;
                                                                font-size:14px;
                                                                line-height:20px;
                                                                color:#667085;
                                                            "
                                                        >
                                                            Password
                                                        </td>

                                                        <td
                                                            valign="top"
                                                            style="
                                                                padding:7px 0;
                                                                font-family:Arial,Helvetica,sans-serif;
                                                                font-size:14px;
                                                                line-height:20px;
                                                                color:#172033;
                                                            "
                                                        >
                                                            The password you create using the link above
                                                        </td>
                                                    </tr>

                                                </table>

                                            </td>
                                        </tr>

                                    </table>

                                </td>
                            </tr>

                            <tr>
                                <td
                                    style="
                                        padding-bottom:10px;
                                        font-family:Arial,Helvetica,sans-serif;
                                        font-size:13px;
                                        line-height:20px;
                                        color:#475467;
                                    "
                                >
                                    For security reasons, this password setup link is
                                    intended only for the recipient of this email.
                                </td>
                            </tr>

                            <tr>
                                <td
                                    style="
                                        padding-bottom:26px;
                                        font-family:Arial,Helvetica,sans-serif;
                                        font-size:13px;
                                        line-height:20px;
                                        color:#475467;
                                    "
                                >
                                    If you were not expecting portal access, please
                                    contact Pakistan Cables.
                                </td>
                            </tr>

                            <tr>
                                <td
                                    style="
                                        font-family:Arial,Helvetica,sans-serif;
                                        font-size:14px;
                                        line-height:20px;
                                        color:#172033;
                                    "
                                >
                                    Regards,<br>
                                    <strong>Pakistan Cables Limited</strong><br>
                                    Business Partner's Portal
                                </td>
                            </tr>

                        </table>

                    </td>
                </tr>

            </table>

        </td>
    </tr>
</table>

</body>
</html>
""";

        await SendEmailAsync(
            email,
            subject,
            body,
            ct);
    }

    public Task SendPasswordResetAsync(string email, string resetUrl, CancellationToken ct = default) =>
        SendEmailAsync(email, "Pakistan Cables Portal Password Reset", $"<p>Use this link to reset your password. It expires in 30 minutes.</p><p><a href=\"{WebUtility.HtmlEncode(resetUrl)}\">Reset Password</a></p>", ct);

    public async Task SendLoginOtpAsync(
        string email,
        string otp,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Login OTP email send started. Recipient={Recipient}",
            email);

        await SendEmailAsync(
            email,
            "Pakistan Cables Portal Device Verification Code",
            $"<p>Your device verification code is <strong>{WebUtility.HtmlEncode(otp)}</strong>.</p><p>This code expires in 10 minutes.</p>",
            ct);
    }

    // ============================================================
    // COMMON SMTP SENDER
    // ============================================================

    private async Task SendEmailAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken ct)
    {
        var smtpEnabled =
            GetBool(
                "SMTP_ENABLED",
                true);

        if (!smtpEnabled)
        {
            _logger.LogWarning(
                "SMTP disabled. Email not sent to {Recipient}.",
                recipient);

            return;
        }

        var host =
            GetSetting(
                "SMTP_HOST");

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                "SMTP_HOST is required.");
        }

        var port =
            GetInt(
                "SMTP_PORT",
                25);

        var enableSsl =
            GetBool(
                "SMTP_ENABLE_SSL",
                false);

        var useDefaultCredentials =
            GetBool(
                "SMTP_USE_DEFAULT_CREDENTIALS",
                true);

        var username =
            GetSetting(
                "SMTP_USERNAME");

        var password =
            GetSetting(
                "SMTP_PASSWORD");

        var from =
            GetSetting(
                "SMTP_FROM");

        if (string.IsNullOrWhiteSpace(from))
        {
            from = username;
        }

        if (string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException(
                "SMTP_FROM or SMTP_USERNAME is required.");
        }

        using var message =
            new MailMessage
            {
                From =
                    new MailAddress(
                        from,
                        "Pakistan Cables"),

                Subject =
                    subject,

                Body =
                    htmlBody,

                IsBodyHtml =
                    true
            };

        message.To.Add(
            new MailAddress(
                recipient));

        using var smtp =
            new SmtpClient(
                host,
                port)
            {
                EnableSsl =
                    enableSsl,

                DeliveryMethod =
                    SmtpDeliveryMethod.Network,

                UseDefaultCredentials =
                    useDefaultCredentials,

                Timeout =
                    30000
            };

        if (!useDefaultCredentials)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException(
                    "SMTP_USERNAME is required when " +
                    "SMTP_USE_DEFAULT_CREDENTIALS=false.");
            }

            smtp.Credentials =
                new NetworkCredential(
                    username,
                    password ?? string.Empty);
        }

        _logger.LogInformation(
            "Sending SMTP email to {Recipient}. Host={Host}, Port={Port}, SSL={SSL}",
            recipient,
            host,
            port,
            enableSsl);

        try
        {
            await smtp.SendMailAsync(
                message,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "SMTP email send failed. Recipient={Recipient} Host={Host} Port={Port} SSL={SSL} FailureKind={FailureKind}",
                recipient,
                host,
                port,
                enableSsl,
                DescribeSmtpFailure(ex));

            throw;
        }

        _logger.LogInformation(
            "Email sent successfully to {Recipient}.",
            recipient);
    }

    // ============================================================
    // CONFIG HELPERS
    // ============================================================

    private string? GetSetting(
        string key)
    {
        var env =
            Environment
                .GetEnvironmentVariable(
                    key);

        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        var value =
            _config[key];

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string DescribeSmtpFailure(Exception ex)
    {
        if (ex is SmtpFailedRecipientException recipientException &&
            recipientException.Message.Contains("relay", StringComparison.OrdinalIgnoreCase))
        {
            return "RelayDenied";
        }

        if (ex is SmtpException smtpException)
        {
            return smtpException.StatusCode switch
            {
                SmtpStatusCode.MustIssueStartTlsFirst => "TlsRequired",
                SmtpStatusCode.ClientNotPermitted => "AuthenticationOrRelayDenied",
                SmtpStatusCode.MailboxUnavailable => "MailboxUnavailableOrRelayDenied",
                _ => $"Smtp:{smtpException.StatusCode}"
            };
        }

        if (ex is TimeoutException)
        {
            return "Timeout";
        }

        if (ex.GetType().Name.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
        {
            return "TlsOrAuthenticationFailure";
        }

        return "ConnectionOrTransportFailure";
    }

    private bool GetBool(
        string key,
        bool defaultValue)
    {
        var value =
            GetSetting(key);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return bool.TryParse(
            value,
            out var result)
            ? result
            : defaultValue;
    }

    private int GetInt(
        string key,
        int defaultValue)
    {
        var value =
            GetSetting(key);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(
            value,
            out var result)
            ? result
            : defaultValue;
    }
}
