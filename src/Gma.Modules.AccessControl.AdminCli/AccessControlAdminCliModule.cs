namespace Gma.Modules.AccessControl.AdminCli;

using System.CommandLine;
using Gma.Modules.AccessControl.Admin.Contracts;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Queries;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Gma.Framework.AccessControl;
using Gma.Framework.Administration;
using Gma.Framework.Administration.AccessControl;
using Gma.Framework.Administration.Cli;
using Gma.Framework.Cqrs;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Pagination;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Results;

public sealed class AccessControlAdminCliModule : IAdminCliModule
{
    private const string AuditFailureMessage = "Admin audit failed.";

    public string Name => AccessControlModuleMetadata.Name;

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.SelectModuleProfile(AccessControlProfiles.Default, "Gma.Modules.AccessControl.AdminCli");
        builder.Services.AddGmaAccessControlAdministrationAuthorization();
        builder.Services.AddAccessControlApplication(builder.Configuration);
        builder.AddAccessControlPersistence();
    }

    public void MapCommands(IAdminCliCommandRegistry commands)
    {
        AdminCliGlobalOptions globalOptions = commands.Services.GetRequiredService<AdminCliGlobalOptions>();
        Command roles = new("roles", "Manage access-control roles.")
        {
            CreateRoleCreateCommand(commands.Services),
            CreateRoleGrantCommand(commands.Services),
            CreateRoleRevokeCommand(commands.Services),
            CreateRoleAssignCommand(commands.Services),
            CreateRoleUnassignCommand(commands.Services),
            CreateRoleAssignmentsCommand(commands.Services, globalOptions),
            CreateRoleListCommand(commands.Services, globalOptions)
        };
        Command admin = new(AccessControlModuleMetadata.AdminSurfaceName, "Access-control administration operations.")
        {
            CreateBootstrapCommand(commands.Services, globalOptions),
            roles
        };

        commands.AddCommand(this.Name, admin);
    }

    private static Command CreateBootstrapCommand(IServiceProvider services, AdminCliGlobalOptions globalOptions)
    {
        Option<bool> yesOption = new("--yes")
        {
            Description = "Confirm first-owner bootstrap."
        };
        Command command = new("bootstrap", "Bootstrap the first owner principal.")
        {
            yesOption
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string? actorId = parseResult.GetValue(globalOptions.ActorOption);

            if (string.IsNullOrWhiteSpace(actorId))
            {
                AdminCliOutput.WriteError("--actor is required for admin bootstrap.");
                return AdminExitCodes.ValidationFailed;
            }

            if (!AdminActor.TrySystem(actorId, out AdminActor? actor))
            {
                AdminCliOutput.WriteError(AdminActor.InvalidIdMessage);
                return AdminExitCodes.ValidationFailed;
            }

            using IServiceScope scope = services.CreateScope();
            IServiceProvider provider = scope.ServiceProvider;
            IAdminActorContextAccessor actorContext = provider.GetRequiredService<IAdminActorContextAccessor>();
            IAdminAuditSink auditSink = provider.GetRequiredService<IAdminAuditSink>();
            ISystemClock clock = provider.GetRequiredService<ISystemClock>();
            IIdGenerator idGenerator = provider.GetRequiredService<IIdGenerator>();
            IRequestDispatcher dispatcher = provider.GetRequiredService<IRequestDispatcher>();
            AdminOperation operation = AdminOperation.Create(
                AccessControlAdminOperationNames.Bootstrap,
                AccessControlAdminPermissions.Bootstrap);

            actorContext.SetActor(actor);
            Result<Unit> result = await dispatcher
                .SendAsync(new BootstrapOwnerCommand(actor.Id, parseResult.GetValue(yesOption)), cancellationToken)
                .ConfigureAwait(false);

            await RecordBootstrapAuditAsync(auditSink, clock, idGenerator, actor, operation, result, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                AdminCliOutput.WriteError(result.Error.Message);
                return AdminExitCodes.Failed;
            }

            AdminCliOutput.WriteMessage($"Bootstrapped owner principal '{actor.Id}'.");
            return AdminExitCodes.Success;
        });

        return command;
    }

    private static Command CreateRoleCreateCommand(IServiceProvider services)
    {
        Option<string> nameOption = new("--name")
        {
            Description = "Role name.",
            Required = true
        };
        Command command = new("create", "Create an access-control role.")
        {
            nameOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            AdminCliExecutor executor = services.GetRequiredService<AdminCliExecutor>();
            string name = parseResult.GetRequiredValue(nameOption);

            return executor.ExecuteAsync(
                parseResult,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesCreate, AccessControlAdminPermissions.RolesManage),
                null,
                requireTenant: false,
                async (provider, token) =>
                {
                    IRequestDispatcher dispatcher = provider.GetRequiredService<IRequestDispatcher>();
                    Result<AccessControlRoleDetails> result = await dispatcher.SendAsync(new CreateRoleCommand(name), token)
                        .ConfigureAwait(false);

                    if (result.IsSuccess)
                    {
                        AdminCliOutput.WriteMessage($"Created role '{result.Value.Name}'.");
                    }

                    return result;
                },
                cancellationToken);
        });

        return command;
    }

    private static Command CreateRoleGrantCommand(IServiceProvider services)
    {
        Option<string> roleOption = new("--role")
        {
            Description = "Role name.",
            Required = true
        };
        Option<string> permissionOption = new("--permission")
        {
            Description = "Permission code to grant.",
            Required = true
        };
        Command command = new("grant", "Grant a permission to a role.")
        {
            roleOption,
            permissionOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            AdminCliExecutor executor = services.GetRequiredService<AdminCliExecutor>();

            return executor.ExecuteAsync(
                parseResult,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesGrant, AccessControlAdminPermissions.RolesManage),
                null,
                requireTenant: false,
                async (provider, token) =>
                {
                    IRequestDispatcher dispatcher = provider.GetRequiredService<IRequestDispatcher>();
                    Result<Unit> result = await dispatcher.SendAsync(
                        new GrantRolePermissionCommand(
                            parseResult.GetRequiredValue(roleOption),
                            parseResult.GetRequiredValue(permissionOption)),
                        token).ConfigureAwait(false);

                    if (result.IsSuccess)
                    {
                        AdminCliOutput.WriteMessage("Permission granted.");
                    }

                    return result;
                },
                cancellationToken);
        });

        return command;
    }

    private static Command CreateRoleRevokeCommand(IServiceProvider services)
    {
        Option<string> roleOption = new("--role")
        {
            Description = "Role name.",
            Required = true
        };
        Option<string> permissionOption = new("--permission")
        {
            Description = "Permission code to revoke.",
            Required = true
        };
        Command command = new("revoke", "Revoke a permission from a role.")
        {
            roleOption,
            permissionOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            AdminCliExecutor executor = services.GetRequiredService<AdminCliExecutor>();

            return executor.ExecuteAsync(
                parseResult,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesRevoke, AccessControlAdminPermissions.RolesManage),
                null,
                requireTenant: false,
                async (provider, token) =>
                {
                    IRequestDispatcher dispatcher = provider.GetRequiredService<IRequestDispatcher>();
                    Result<Unit> result = await dispatcher.SendAsync(
                        new RevokeRolePermissionCommand(
                            parseResult.GetRequiredValue(roleOption),
                            parseResult.GetRequiredValue(permissionOption)),
                        token).ConfigureAwait(false);

                    if (result.IsSuccess)
                    {
                        AdminCliOutput.WriteMessage("Permission revoked.");
                    }

                    return result;
                },
                cancellationToken);
        });

        return command;
    }

    private static Command CreateRoleAssignCommand(IServiceProvider services)
    {
        Option<string> targetKindOption = new("--target-kind")
        {
            Description = "Subject kind: user, admin-actor, service, or system.",
            DefaultValueFactory = _ => AccessSubjectKindNames.AdminActor
        };
        Option<string> targetIdOption = new("--target-id", "--target-actor")
        {
            Description = "Subject id that receives the role. --target-actor remains a compatibility alias.",
            Required = true
        };
        Option<string> roleOption = new("--role")
        {
            Description = "Role name.",
            Required = true
        };
        Option<string?> scopeOption = new("--scope")
        {
            Description = "Access scope for the assignment, such as 'global' or 'tenant:tenant-a'. Omit for global."
        };
        Command command = new("assign", "Assign a role to an access subject.")
        {
            targetKindOption,
            targetIdOption,
            roleOption,
            scopeOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            AdminCliExecutor executor = services.GetRequiredService<AdminCliExecutor>();
            string? scope = parseResult.GetValue(scopeOption);

            return executor.ExecuteAsync(
                parseResult,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesAssign, AccessControlAdminPermissions.RolesManage),
                null,
                requireTenant: false,
                async (provider, token) =>
                {
                    if (!AccessSubjectKindNames.TryParse(
                            parseResult.GetRequiredValue(targetKindOption),
                            out AccessSubjectKind subjectKind))
                    {
                        return Result.Failure<Unit>(AccessControlApplicationErrors.SubjectInvalid);
                    }

                    if (!TryParseScope(scope, out AccessScope? accessScope))
                    {
                        return Result.Failure<Unit>(AccessControlApplicationErrors.ScopeInvalid);
                    }

                    IRequestDispatcher dispatcher = provider.GetRequiredService<IRequestDispatcher>();
                    Result<Unit> result = await dispatcher.SendAsync(
                        new AssignRoleCommand(
                            subjectKind,
                            parseResult.GetRequiredValue(targetIdOption),
                            parseResult.GetRequiredValue(roleOption),
                            accessScope),
                        token).ConfigureAwait(false);

                    if (result.IsSuccess)
                    {
                        AdminCliOutput.WriteMessage("Role assigned.");
                    }

                    return result;
                },
                cancellationToken);
        });

        return command;
    }

    private static Command CreateRoleUnassignCommand(IServiceProvider services)
    {
        Option<string> targetKindOption = new("--target-kind")
        {
            Description = "Subject kind: user, admin-actor, service, or system.",
            DefaultValueFactory = _ => AccessSubjectKindNames.AdminActor
        };
        Option<string> targetIdOption = new("--target-id", "--target-actor")
        {
            Description = "Subject id that loses the role. --target-actor remains a compatibility alias.",
            Required = true
        };
        Option<string> roleOption = new("--role")
        {
            Description = "Role name.",
            Required = true
        };
        Option<string?> scopeOption = new("--scope")
        {
            Description = "Exact access scope of the assignment. Omit for global."
        };
        Command command = new("unassign", "Remove an exact role assignment from an access subject.")
        {
            targetKindOption,
            targetIdOption,
            roleOption,
            scopeOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            AdminCliExecutor executor = services.GetRequiredService<AdminCliExecutor>();
            string? scope = parseResult.GetValue(scopeOption);

            return executor.ExecuteAsync(
                parseResult,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesUnassign, AccessControlAdminPermissions.RolesManage),
                null,
                requireTenant: false,
                async (provider, token) =>
                {
                    if (!AccessSubjectKindNames.TryParse(
                            parseResult.GetRequiredValue(targetKindOption),
                            out AccessSubjectKind subjectKind))
                    {
                        return Result.Failure<Unit>(AccessControlApplicationErrors.SubjectInvalid);
                    }

                    if (!TryParseScope(scope, out AccessScope? accessScope))
                    {
                        return Result.Failure<Unit>(AccessControlApplicationErrors.ScopeInvalid);
                    }

                    IRequestDispatcher dispatcher = provider.GetRequiredService<IRequestDispatcher>();
                    Result<Unit> result = await dispatcher.SendAsync(
                        new UnassignRoleCommand(
                            subjectKind,
                            parseResult.GetRequiredValue(targetIdOption),
                            parseResult.GetRequiredValue(roleOption),
                            accessScope),
                        token).ConfigureAwait(false);

                    if (result.IsSuccess)
                    {
                        AdminCliOutput.WriteMessage("Role unassigned.");
                    }

                    return result;
                },
                cancellationToken);
        });

        return command;
    }

    private static bool TryParseScope(string? value, out AccessScope? scope)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            scope = null;
            return true;
        }

        if (AccessScope.TryParse(value, out AccessScope? parsedScope))
        {
            scope = parsedScope;
            return true;
        }

        scope = null;
        return false;
    }

    private static Command CreateRoleAssignmentsCommand(
        IServiceProvider services,
        AdminCliGlobalOptions globalOptions)
    {
        Option<string> roleOption = new("--role")
        {
            Description = "Role name.",
            Required = true
        };
        Option<int> pageOption = PageOption();
        Option<int> pageSizeOption = PageSizeOption();
        Command command = new("assignments", "List assignments for a role.")
        {
            roleOption,
            pageOption,
            pageSizeOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            AdminCliExecutor executor = services.GetRequiredService<AdminCliExecutor>();

            return executor.ExecuteAsync(
                parseResult,
                AdminOperation.Create(AccessControlAdminOperationNames.RoleAssignmentsList, AccessControlAdminPermissions.RolesRead),
                null,
                requireTenant: false,
                async (provider, token) =>
                {
                    IRequestDispatcher dispatcher = provider.GetRequiredService<IRequestDispatcher>();
                    Result<AccessControlPage<AccessControlRoleAssignmentDetails>> result = await dispatcher
                        .QueryAsync(new ListRoleAssignmentsQuery(
                            parseResult.GetRequiredValue(roleOption),
                            parseResult.GetValue(pageOption),
                            parseResult.GetValue(pageSizeOption)), token)
                        .ConfigureAwait(false);

                    if (result.IsSuccess)
                    {
                        AdminCliOutput.WriteRows(
                            result.Value.Items,
                            parseResult.GetValue(globalOptions.OutputOption) ?? AdminCliOutput.Table,
                            [
                                ("Subject kind", assignment => AccessSubjectKindNames.GetName(assignment.SubjectKind)),
                                ("Subject id", assignment => assignment.SubjectId),
                                ("Role", assignment => assignment.RoleName),
                                ("Scope", assignment => assignment.AccessScope.Value),
                                ("Created (UTC)", assignment => assignment.CreatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
                            ]);
                    }

                    return result;
                },
                cancellationToken);
        });

        return command;
    }

    private static Command CreateRoleListCommand(IServiceProvider services, AdminCliGlobalOptions globalOptions)
    {
        Option<int> pageOption = PageOption();
        Option<int> pageSizeOption = PageSizeOption();
        Command command = new("list", "List access-control roles.")
        {
            pageOption,
            pageSizeOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            AdminCliExecutor executor = services.GetRequiredService<AdminCliExecutor>();

            return executor.ExecuteAsync(
                parseResult,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesList, AccessControlAdminPermissions.RolesRead),
                null,
                requireTenant: false,
                async (provider, token) =>
                {
                    IRequestDispatcher dispatcher = provider.GetRequiredService<IRequestDispatcher>();
                    Result<AccessControlPage<AccessControlRoleDetails>> result = await dispatcher.QueryAsync(
                        new ListRolesQuery(parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption)), token)
                        .ConfigureAwait(false);

                    if (result.IsSuccess)
                    {
                        AdminCliOutput.WriteRows(
                            result.Value.Items,
                            parseResult.GetValue(globalOptions.OutputOption) ?? AdminCliOutput.Table,
                            [
                                ("Name", role => role.Name),
                                ("Permissions", role => string.Join(",", role.Permissions)),
                                ("Assignments", role => role.AssignmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            ]);
                    }

                    return result;
                },
                cancellationToken);
        });

        return command;
    }

    private static Option<int> PageOption() => new("--page")
    {
        Description = "Page number.",
        DefaultValueFactory = _ => PageRequest.DefaultPage
    };

    private static Option<int> PageSizeOption() => new("--page-size")
    {
        Description = "Page size.",
        DefaultValueFactory = _ => PageRequest.DefaultPageSize
    };

    private static async Task RecordBootstrapAuditAsync(
        IAdminAuditSink auditSink,
        ISystemClock clock,
        IIdGenerator idGenerator,
        AdminActor actor,
        AdminOperation operation,
        Result result,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditSink.RecordAsync(
                new AdminAuditRecord(
                    idGenerator.NewId(),
                    actor.Id,
                    null,
                    operation.Name,
                    operation.Permission.Code,
                    result.IsSuccess ? AdminAuditResult.Succeeded : AdminAuditResult.Failed,
                    result.IsSuccess ? null : result.Error.Code,
                    clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AdminCliOutput.WriteError(AuditFailureMessage);
        }
    }
}
