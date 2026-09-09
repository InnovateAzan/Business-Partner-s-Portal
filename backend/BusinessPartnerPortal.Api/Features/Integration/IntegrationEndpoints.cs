using BusinessPartnerPortal.Api.Data;
using BusinessPartnerPortal.Api.Security;

using Microsoft.EntityFrameworkCore;

namespace BusinessPartnerPortal.Api.Features.Integration;

public static class IntegrationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group =
            app.MapGroup(
                    "/api/v1/integration"
                )
                .RequireAuthorization()
                .WithTags(
                    "Integration"
                );

        // ========================================================
        // INTEGRATION STATUS
        // ========================================================

        group.MapGet(
            "/status",
            async (
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                await current.DemandAsync(
                    "INTEGRATION.VIEW",
                    ct
                );

                var connection =
                    db.Database
                        .GetDbConnection();

                if (
                    connection.State !=
                    System.Data
                        .ConnectionState
                        .Open
                )
                {
                    await connection
                        .OpenAsync(
                            ct
                        );
                }

                await using var command =
                    connection
                        .CreateCommand();

                command.CommandText =
                    """
                    SELECT
                        id,
                        event_type,
                        aggregate_id,
                        status,
                        attempt_count,
                        last_error,
                        created_at,
                        next_attempt_at,
                        processed_at,
                        external_reference,
                        oracle_request_id,
                        oracle_group_id,
                        correlation_id

                    FROM
                        integration.outbox_messages

                    ORDER BY
                        created_at DESC

                    LIMIT
                        500
                    """;

                var list =
                    new List<object>();

                await using var reader =
                    await command
                        .ExecuteReaderAsync(
                            ct
                        );

                while (
                    await reader.ReadAsync(
                        ct
                    )
                )
                {
                    list.Add(
                        new
                        {
                            id =
                                reader.GetGuid(
                                    0
                                ),

                            eventType =
                                reader.GetString(
                                    1
                                ),

                            aggregateId =
                                reader.GetGuid(
                                    2
                                ),

                            status =
                                reader.GetString(
                                    3
                                ),

                            attemptCount =
                                reader.GetInt32(
                                    4
                                ),

                            lastError =
                                reader.IsDBNull(
                                    5
                                )
                                    ?
                                    null
                                    :
                                    reader.GetString(
                                        5
                                    ),

                            createdAt =
                                reader
                                    .GetFieldValue<
                                        DateTimeOffset
                                    >(
                                        6
                                    ),

                            nextAttemptAt =
                                reader.IsDBNull(
                                    7
                                )
                                    ?
                                    (DateTimeOffset?)
                                    null
                                    :
                                    reader
                                        .GetFieldValue<
                                            DateTimeOffset
                                        >(
                                            7
                                        ),

                            processedAt =
                                reader.IsDBNull(
                                    8
                                )
                                    ?
                                    (DateTimeOffset?)
                                    null
                                    :
                                    reader
                                        .GetFieldValue<
                                            DateTimeOffset
                                        >(
                                            8
                                        ),

                            externalReference =
                                reader.IsDBNull(
                                    9
                                )
                                    ?
                                    null
                                    :
                                    reader.GetString(
                                        9
                                    ),

                            oracleRequestId =
                                reader.IsDBNull(
                                    10
                                )
                                    ?
                                    (long?)
                                    null
                                    :
                                    reader.GetInt64(
                                        10
                                    ),

                            oracleGroupId =
                                reader.IsDBNull(
                                    11
                                )
                                    ?
                                    null
                                    :
                                    reader.GetString(
                                        11
                                    ),

                            correlationId =
                                reader.GetGuid(
                                    12
                                )
                        }
                    );
                }

                return Results.Ok(
                    list
                );
            }
        );

        // ========================================================
        // MANUAL RETRY
        // ========================================================

        group.MapPost(
            "/{id:guid}/retry",
            async (
                Guid id,
                CurrentUser current,
                AppDbContext db,
                CancellationToken ct) =>
            {
                await current.DemandAsync(
                    "INTEGRATION.RETRY",
                    ct
                );

                var updated =
                    await db.Database
                        .ExecuteSqlInterpolatedAsync(
                            $"""
                            UPDATE
                                integration.outbox_messages

                            SET
                                status =
                                'PENDING',

                                next_attempt_at =
                                now(),

                                last_error =
                                NULL,

                                processed_at =
                                NULL

                            WHERE
                                id =
                                {id}

                            AND
                                status IN
                                (
                                    'FAILED',
                                    'RETRYING'
                                )
                            """,
                            ct
                        );

                if (
                    updated == 0
                )
                {
                    return Results.NotFound(
                        new
                        {
                            message =
                                "Integration item was not found or cannot be retried."
                        }
                    );
                }

                return Results.Ok(
                    new
                    {
                        retried =
                            true,

                        id
                    }
                );
            }
        );

        return app;
    }
}