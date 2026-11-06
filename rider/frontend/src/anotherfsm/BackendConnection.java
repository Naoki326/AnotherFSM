package anotherfsm;

import com.anotherfsm.rider.protocol.AnotherFsmModel;
import com.anotherfsm.rider.protocol.AnotherFsmModel_GeneratedKt;
import com.intellij.openapi.Disposable;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.application.ModalityState;
import com.intellij.openapi.components.Service;
import com.intellij.openapi.diagnostic.Logger;
import com.intellij.openapi.project.Project;
import com.jetbrains.rd.framework.IProtocol;
import com.jetbrains.rd.framework.RdTaskResult;
import com.jetbrains.rd.ide.model.Solution;
import com.jetbrains.rd.ide.model.SolutionModel;
import com.jetbrains.rd.util.lifetime.Lifetime;
import com.jetbrains.rd.util.lifetime.LifetimeDefinition;
import com.jetbrains.rd.util.lifetime.LifetimeStatus;
import com.jetbrains.rider.projectView.SolutionHostExtensionsKt;
import com.jetbrains.rider.protocol.ApplicationProtocolKt;
import com.jetbrains.rider.protocol.IProtocolHost;
import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.Objects;
import java.util.Set;
import java.util.concurrent.CancellationException;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.Consumer;
import kotlin.Unit;
import kotlin.jvm.JvmClassMappingKt;

/**
 * Uses only the existing host protocol. Model acquisition and RD subscriptions run on its scheduler;
 * consumer callbacks run later on EDT. No synchronous RPC, user project loading or business execution.
 *
 * Backend invalidated JSON is forwarded verbatim. Local Solution/transport changes instead emit
 * {"transportInvalidated":true}: deliberately no invented session/contextVersion. Consumers must
 * treat this marker as unconditional invalidation, and validate backend versions themselves.
 * This service does not retry a semantic query across a Solution/connection epoch.
 */
@Service(Service.Level.PROJECT)
public final class BackendConnection implements Disposable {
    private static final Logger LOG = Logger.getInstance(BackendConnection.class);
    private static final String TRANSPORT_INVALIDATED = "{\"transportInvalidated\":true}";
    private final Project project;
    private final LifetimeDefinition service = new LifetimeDefinition();
    private final CopyOnWriteArrayList<Consumer<String>> listeners = new CopyOnWriteArrayList<>();
    private final AtomicReference<Binding> current = new AtomicReference<>();
    private final AtomicBoolean watching = new AtomicBoolean();
    private final AtomicBoolean hasBound = new AtomicBoolean();
    private final AtomicBoolean disposed = new AtomicBoolean();

    public BackendConnection(Project project) {
        this.project = project;
    }

    public static BackendConnection get(Project project) {
        return project.getService(BackendConnection.class);
    }

    /** Returns before host lookup, model binding or RPC; disposing the handle cancels its RD lifetime. */
    public Disposable query(String envelope, Consumer<String> success, Consumer<Throwable> failure) {
        Request request = new Request(Objects.requireNonNull(success), Objects.requireNonNull(failure));
        Objects.requireNonNull(envelope);
        ensureWatching();
        later(() -> dispatch(request, envelope));
        return request;
    }

    /** Project/service lifetime subscription; the caller normally registers once. */
    public void onInvalidated(Consumer<String> listener) {
        Objects.requireNonNull(listener);
        if (!live()) return;
        listeners.add(listener);
        if (!live()) listeners.remove(listener);
        ensureWatching();
    }

    private boolean live() {
        return !disposed.get() && !project.isDisposed() && alive(service);
    }

    private static boolean alive(Lifetime lifetime) {
        return lifetime != null && lifetime.getStatus() == LifetimeStatus.Alive;
    }

    private void later(Runnable action) {
        ApplicationManager.getApplication().invokeLater(() -> {
            if (live()) action.run();
        }, ModalityState.any());
    }

    private void ensureWatching() {
        if (!live() || !watching.compareAndSet(false, true)) return;
        later(() -> {
            // Observe existing/future host attachments, without getOrCreate/create/startBackend.
            ApplicationProtocolKt.getProtocolManager(ApplicationManager.getApplication())
                .getProtocolHosts().view(service, (membership, host) -> {
                    HostWatch watch = new HostWatch(host, membership);
                    host.getProject().advise(watch.lifetime, attachedProject -> {
                        watch.queue(watch::inspect);
                        return Unit.INSTANCE;
                    });
                    return Unit.INSTANCE;
                });
        });
    }

    /** Called on EDT; getProtocolHostIfExists never creates a backend/protocol. */
    private void dispatch(Request request, String envelope) {
        if (!request.live()) return;
        try {
            IProtocolHost host = ApplicationProtocolKt.getProtocolHostIfExists(project);
            if (host == null) throw new IllegalStateException("Rider backend is not attached to this project");
            host.getProtocol().getScheduler().queue(() -> {
                if (request.live()) {
                    try {
                        Binding binding = acquire(host);
                        if (binding == null || binding.disconnected)
                            throw new CancellationException("Rider Solution/backend is not available");
                        binding.requests.attach(request.lifetime, true);
                        if (request.live() && current.get() == binding) {
                            binding.model.getQuery().start(request.lifetime, envelope,
                                    host.getProtocol().getScheduler())
                                .getResult().advise(request.lifetime, result -> {
                                    request.complete(binding, result);
                                    return Unit.INSTANCE;
                                });
                        } else {
                            request.dispose();
                        }
                    } catch (RuntimeException error) {
                        request.deliver(null, null, error);
                    }
                }
                return Unit.INSTANCE;
            });
        } catch (RuntimeException error) {
            request.deliver(null, null, error);
        }
    }

    /** Only called on this host's scheduler; never use a cached model to start a query. */
    private Binding acquire(IProtocolHost host) {
        host.getProtocol().getScheduler().assertThread(this);
        if (!live() || !alive(host.getLifetime()) ||
                ApplicationProtocolKt.getProtocolHostIfExists(project) != host ||
                !SolutionHostExtensionsKt.getHasSolution(project)) {
            dropForHost(host);
            return null;
        }
        Solution solution = SolutionHostExtensionsKt.getSolution(project);
        if (!solution.isBound()) {
            dropForHost(host);
            return null;
        }
        AnotherFsmModel model = AnotherFsmModel_GeneratedKt.getAnotherFsmModel(solution);
        Lifetime modelLifetime = model.getBindLifetime();
        if (!model.isBound() || !alive(modelLifetime)) {
            dropForHost(host);
            return null;
        }
        Binding old = current.get();
        if (old != null && old.host == host && old.solution == solution && old.model == model && alive(old.lifetime))
            return old;
        Binding next = new Binding(host, solution, model, modelLifetime);
        if (!current.compareAndSet(old, next)) {
            next.lifetime.terminate(true);
            throw new CancellationException("Rider backend binding changed concurrently");
        }
        if (old != null) old.lifetime.terminate(true);
        next.lifetime.onTerminationIfAlive(() -> {
            if (current.compareAndSet(next, null)) invalidate(null, TRANSPORT_INVALIDATED);
            return Unit.INSTANCE;
        });
        // Async signal's real API accepts an explicit scheduler (no assumed callback thread).
        model.getInvalidated().adviseOn(next.lifetime, host.getProtocol().getScheduler(), json -> {
            invalidate(next, json);
            return Unit.INSTANCE;
        });
        model.getConnected().advise(next.lifetime, connected -> {
            host.getProtocol().getScheduler().queue(() -> {
                if (current.get() == next && alive(next.lifetime)) {
                    next.modelConnected = connected;
                    next.connectionChanged();
                }
                return Unit.INSTANCE;
            });
            return Unit.INSTANCE;
        });
        host.getProtocol().getWire().getConnected().advise(next.lifetime, connected -> {
            host.getProtocol().getScheduler().queue(() -> {
                if (current.get() == next && alive(next.lifetime)) {
                    next.wireConnected = connected;
                    next.everWireConnected |= connected;
                    next.connectionChanged();
                }
                return Unit.INSTANCE;
            });
            return Unit.INSTANCE;
        });
        if (!live() || !alive(next.lifetime) ||
                ApplicationProtocolKt.getProtocolHostIfExists(project) != host) {
            dropForHost(host);
            return null;
        }
        // Initial attachment has no old semantic state to invalidate (and must not cancel the first query).
        if (hasBound.getAndSet(true)) invalidate(null, TRANSPORT_INVALIDATED);
        return next;
    }

    private void dropForHost(IProtocolHost host) {
        Binding binding = current.get();
        if (binding != null && binding.host == host && current.compareAndSet(binding, null)) {
            binding.lifetime.terminate(true);
            invalidate(null, TRANSPORT_INVALIDATED);
        }
    }

    private void invalidate(Binding source, String json) {
        later(() -> {
            if (source != null && (current.get() != source || !alive(source.lifetime))) return;
            for (Consumer<String> listener : listeners) {
                if (!live()) return;
                try { listener.accept(json); }
                catch (RuntimeException error) { LOG.warn("AnotherFSM invalidation listener failed", error); }
            }
        });
    }

    /** Host membership follows reattachment; observing roots never constructs a replacement root. */
    private final class HostWatch {
        final IProtocolHost host;
        final IProtocol protocol;
        final LifetimeDefinition lifetime;
        final Set<SolutionModel> roots = Collections.newSetFromMap(new IdentityHashMap<>());
        boolean subscribed; // protocol scheduler confined

        HostWatch(IProtocolHost host, Lifetime membership) {
            this.host = host;
            this.protocol = host.getProtocol();
            lifetime = Lifetime.Companion.defineIntersection(service, membership);
            host.getLifetime().attach(lifetime, true);
            lifetime.onTerminationIfAlive(() -> {
                dropForHost(host);
                return Unit.INSTANCE;
            });
        }

        void queue(Runnable action) {
            if (!alive(lifetime) || !live()) return;
            protocol.getScheduler().queue(() -> {
                if (alive(lifetime) && live()) {
                    try { action.run(); }
                    catch (RuntimeException error) {
                        dropForHost(host);
                        LOG.warn("AnotherFSM backend lifecycle observation failed", error);
                    }
                }
                return Unit.INSTANCE;
            });
        }

        void inspect() {
            if (host.getProject().getValue() != project) {
                dropForHost(host);
                return;
            }
            if (!subscribed) {
                subscribed = true;
                protocol.getExtCreated().advise(lifetime, info -> {
                    // Creation can precede preBind; inspect only after the current scheduler turn.
                    if (info.getInfo().getExt() instanceof SolutionModel) queue(this::inspect);
                    return Unit.INSTANCE;
                });
            }
            SolutionModel root = protocol.tryGetExtension(JvmClassMappingKt.getKotlinClass(SolutionModel.class));
            if (root != null) watchRoot(root);
            acquire(host);
        }

        void watchRoot(SolutionModel root) {
            Lifetime rootLifetime = root.getBindLifetime();
            if (!alive(rootLifetime) || !roots.add(root)) return;
            LifetimeDefinition scope = Lifetime.Companion.defineIntersection(lifetime, rootLifetime);
            scope.onTerminationIfAlive(() -> {
                queue(() -> { roots.remove(root); inspect(); });
                return Unit.INSTANCE;
            });
            root.getSolutions().getChange().advise(scope, event -> {
                // Defer until the host finishes updating its Project -> Solution association.
                queue(this::inspect);
                return Unit.INSTANCE;
            });
            queue(this::inspect);
        }
    }

    private final class Binding {
        final IProtocolHost host;
        final Solution solution;
        final AnotherFsmModel model;
        final LifetimeDefinition lifetime;
        LifetimeDefinition requests; // scheduler confined; a separate epoch after any disconnection
        boolean everConnected;
        boolean everWireConnected;
        boolean modelConnected;
        boolean wireConnected;
        volatile boolean disconnected;

        Binding(IProtocolHost host, Solution solution, AnotherFsmModel model, Lifetime modelLifetime) {
            this.host = host;
            this.solution = solution;
            this.model = model;
            lifetime = Lifetime.Companion.defineIntersection(service, modelLifetime);
            host.getLifetime().attach(lifetime, true);
            requests = lifetime.createNested();
            modelConnected = model.getConnected().getValue();
            wireConnected = host.getProtocol().getWire().getConnected().getValue();
            everWireConnected = wireConnected;
            everConnected = modelConnected && wireConnected;
        }

        void connectionChanged() {
            boolean connected = modelConnected && wireConnected;
            // Losing the host wire also cancels queries still waiting for the first extension handshake.
            if (!connected && (everConnected || (!wireConnected && everWireConnected)) && !disconnected) {
                disconnected = true;
                requests.terminate(true);
                invalidate(null, TRANSPORT_INVALIDATED);
            } else if (connected) {
                everConnected = true;
                if (disconnected) {
                    requests = lifetime.createNested();
                    disconnected = false;
                    invalidate(null, TRANSPORT_INVALIDATED);
                }
            }
        }

        boolean isCurrent() {
            return current.get() == this && alive(lifetime) && !disconnected &&
                ApplicationProtocolKt.getProtocolHostIfExists(project) == host &&
                SolutionHostExtensionsKt.getHasSolution(project) &&
                SolutionHostExtensionsKt.getSolution(project) == solution;
        }
    }

    private final class Request implements Disposable {
        final LifetimeDefinition lifetime = service.createNested();
        final AtomicBoolean completed = new AtomicBoolean();
        final Consumer<String> success;
        final Consumer<Throwable> failure;

        Request(Consumer<String> success, Consumer<Throwable> failure) {
            this.success = success;
            this.failure = failure;
        }

        boolean live() { return BackendConnection.this.live() && alive(lifetime); }

        void complete(Binding binding, RdTaskResult<String> result) {
            // Kotlin unwrap can throw the checked Java Exception RdFault without a throws declaration.
            // Catch Exception, not just RuntimeException: Success -> String, Fault/Cancelled -> failure.
            try { deliver(binding, result.unwrap(), null); }
            catch (Exception error) { deliver(binding, null, error); }
        }

        void deliver(Binding binding, String value, Throwable error) {
            if (!live() || !completed.compareAndSet(false, true)) return;
            later(() -> {
                try {
                    if (!live()) return;
                    if (binding != null && !binding.isCurrent()) {
                        invalidate(null, TRANSPORT_INVALIDATED);
                        return;
                    }
                    if (error == null) success.accept(value);
                    else failure.accept(error);
                } catch (RuntimeException callbackError) {
                    LOG.warn("AnotherFSM query callback failed", callbackError);
                } finally {
                    dispose();
                }
            });
        }

        @Override public void dispose() { lifetime.terminate(true); }
    }

    @Override public void dispose() {
        if (!disposed.compareAndSet(false, true)) return;
        current.set(null);
        listeners.clear();
        service.terminate(true);
    }
}
