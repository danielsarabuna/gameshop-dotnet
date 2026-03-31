(() => {
    const canvas = document.getElementById("shards-bg");
    if (!(canvas instanceof HTMLCanvasElement)) {
        return;
    }

    const ctx = canvas.getContext("2d");
    if (!ctx) {
        return;
    }

    const prefersReducedMotion = !!window.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches;
    const isMobile =
        !!window.matchMedia?.("(max-width: 900px)")?.matches ||
        /Mobi|Android|iPhone|iPad|iPod/i.test(navigator.userAgent);

    const shardCount = isMobile ? 20 : 40;
    const enablePush = !isMobile && !prefersReducedMotion;
    const enableAnimation = !prefersReducedMotion;

    const backgroundColor = "#05040E";
    const pushRadius = 160;
    const glowRadius = 80;
    const maxDpr = 2;

    const mouse = {
        x: 0,
        y: 0,
        active: false,
    };

    const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
    const rand = (min, max) => min + Math.random() * (max - min);

    const randomTriangle = (radius) => {
        const angles = [rand(0, Math.PI * 2), rand(0, Math.PI * 2), rand(0, Math.PI * 2)].sort((a, b) => a - b);
        return angles.map((a) => {
            const r = radius * rand(0.55, 1);
            return { x: Math.cos(a) * r, y: Math.sin(a) * r };
        });
    };

    let width = 1;
    let height = 1;
    let dpr = 1;
    let lastTs = 0;
    let rafId = 0;

    const createShard = () => {
        const radius = rand(15, 50);
        const speed = rand(isMobile ? 6 : 8, isMobile ? 18 : 26);
        const angle = rand(0, Math.PI * 2);

        return {
            x: rand(0, width),
            y: rand(0, height),
            vx: Math.cos(angle) * speed,
            vy: Math.sin(angle) * speed,
            rot: rand(0, Math.PI * 2),
            vr: rand(-0.25, 0.25),
            spinDir: Math.random() < 0.5 ? -1 : 1,
            radius,
            pts: randomTriangle(radius),
            fillHue: rand(250, 280),
            fillL: rand(10, 45),
            fillA: rand(0.15, 0.55),
            strokeHue: rand(260, 290),
            strokeL: rand(60, 85),
            strokeA: rand(0.2, 0.55),
        };
    };

    let shards = [];

    const resize = () => {
        width = Math.max(1, window.innerWidth);
        height = Math.max(1, window.innerHeight);
        dpr = Math.min(maxDpr, window.devicePixelRatio || 1);

        canvas.width = Math.floor(width * dpr);
        canvas.height = Math.floor(height * dpr);
        canvas.style.width = `${width}px`;
        canvas.style.height = `${height}px`;

        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        if (!mouse.active) {
            mouse.x = width * 0.5;
            mouse.y = height * 0.4;
        }
    };

    const wrap = (shard) => {
        const margin = 90;
        if (shard.x < -margin) shard.x = width + margin;
        if (shard.x > width + margin) shard.x = -margin;
        if (shard.y < -margin) shard.y = height + margin;
        if (shard.y > height + margin) shard.y = -margin;
    };

    const update = (dt) => {
        for (const shard of shards) {
            shard.x += shard.vx * dt;
            shard.y += shard.vy * dt;
            shard.rot += shard.vr * dt;

            shard.vx *= 0.999;
            shard.vy *= 0.999;
            shard.vr *= 0.995;

            if (enablePush && mouse.active) {
                const dx = shard.x - mouse.x;
                const dy = shard.y - mouse.y;
                const dist = Math.hypot(dx, dy);
                if (dist > 0.001 && dist < pushRadius) {
                    const t = 1 - dist / pushRadius;
                    const force = t * t * 520; // px/s^2
                    const nx = dx / dist;
                    const ny = dy / dist;
                    shard.vx += nx * force * dt;
                    shard.vy += ny * force * dt;
                    shard.vr += shard.spinDir * t * 8 * dt;
                }
            }

            const speed = Math.hypot(shard.vx, shard.vy);
            const maxSpeed = 160;
            if (speed > maxSpeed) {
                const k = maxSpeed / speed;
                shard.vx *= k;
                shard.vy *= k;
            }

            wrap(shard);
        }
    };

    const drawGlow = () => {
        if (!enablePush || !mouse.active) return;
        const gradient = ctx.createRadialGradient(mouse.x, mouse.y, 0, mouse.x, mouse.y, glowRadius);
        gradient.addColorStop(0, "rgba(160, 100, 255, 0.18)");
        gradient.addColorStop(1, "rgba(160, 100, 255, 0)");
        ctx.fillStyle = gradient;
        ctx.fillRect(mouse.x - glowRadius, mouse.y - glowRadius, glowRadius * 2, glowRadius * 2);
    };

    const drawShard = (shard) => {
        let proximity = 0;
        if (enablePush && mouse.active) {
            const dist = Math.hypot(shard.x - mouse.x, shard.y - mouse.y);
            if (dist < pushRadius) {
                proximity = 1 - dist / pushRadius;
            }
        }

        const fillL = clamp(shard.fillL + 35 * proximity, 0, 100);
        const strokeA = shard.strokeA + (0.8 - shard.strokeA) * proximity;

        ctx.save();
        ctx.translate(shard.x, shard.y);
        ctx.rotate(shard.rot);

        ctx.beginPath();
        ctx.moveTo(shard.pts[0].x, shard.pts[0].y);
        ctx.lineTo(shard.pts[1].x, shard.pts[1].y);
        ctx.lineTo(shard.pts[2].x, shard.pts[2].y);
        ctx.closePath();

        ctx.fillStyle = `hsla(${shard.fillHue.toFixed(0)}, 70%, ${fillL.toFixed(0)}%, ${shard.fillA.toFixed(2)})`;
        ctx.strokeStyle = `hsla(${shard.strokeHue.toFixed(0)}, 80%, ${shard.strokeL.toFixed(0)}%, ${strokeA.toFixed(2)})`;
        ctx.lineWidth = 1;
        ctx.fill();
        ctx.stroke();

        ctx.restore();
    };

    const draw = () => {
        ctx.clearRect(0, 0, width, height);
        ctx.fillStyle = backgroundColor;
        ctx.fillRect(0, 0, width, height);

        if (enablePush && mouse.active) {
            ctx.save();
            ctx.globalCompositeOperation = "lighter";
            drawGlow();
            ctx.restore();
        }

        for (const shard of shards) {
            drawShard(shard);
        }
    };

    const loop = (ts) => {
        const dt = clamp((ts - lastTs) / 1000, 0, 0.033);
        lastTs = ts;
        update(dt);
        draw();
        rafId = window.requestAnimationFrame(loop);
    };

    const init = () => {
        resize();
        shards = Array.from({ length: shardCount }, () => createShard());

        draw();
        if (!enableAnimation) {
            return;
        }

        lastTs = performance.now();
        rafId = window.requestAnimationFrame(loop);
    };

    if (enablePush) {
        window.addEventListener(
            "mousemove",
            (e) => {
                mouse.x = e.clientX;
                mouse.y = e.clientY;
                mouse.active = true;
            },
            { passive: true }
        );

        window.addEventListener(
            "mouseleave",
            () => {
                mouse.active = false;
            },
            { passive: true }
        );
    }

    window.addEventListener("resize", resize, { passive: true });
    window.addEventListener(
        "visibilitychange",
        () => {
            if (!enableAnimation) {
                return;
            }

            if (document.hidden) {
                window.cancelAnimationFrame(rafId);
                rafId = 0;
                return;
            }

            if (rafId === 0) {
                lastTs = performance.now();
                rafId = window.requestAnimationFrame(loop);
            }
        },
        { passive: true }
    );

    init();
})();
