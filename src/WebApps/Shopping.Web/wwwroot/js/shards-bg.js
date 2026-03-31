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

    const heartCount = isMobile ? 20 : 40;
    const enablePush = !isMobile && !prefersReducedMotion;
    const enableAnimation = !prefersReducedMotion;

    const backgroundColor = "#05040E";
    const pushRadius = 160;
    const glowRadius = 90;
    const maxDpr = 2;

    const mouse = {
        x: 0,
        y: 0,
        active: false,
    };

    const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
    const rand = (min, max) => min + Math.random() * (max - min);
    const pow = Math.pow;

    let width = 1;
    let height = 1;
    let dpr = 1;
    let lastTs = 0;
    let rafId = 0;

    const createHeart = () => {
        const size = rand(10, 40);
        const speedPerFrame = rand(0.2, 0.8); // px/frame at 60fps
        const speed = speedPerFrame * 60; // px/s
        const angle = rand(0, Math.PI * 2);

        return {
            x: rand(0, width),
            y: rand(0, height),
            dx: Math.cos(angle) * speed,
            dy: Math.sin(angle) * speed,
            vx: 0,
            vy: 0,
            rot: rand(0, Math.PI * 2),
            drot: rand(-0.005, 0.005) * 60, // rad/s
            vrot: 0,
            spinDir: Math.random() < 0.5 ? -1 : 1,
            phase: rand(0, Math.PI * 2),
            size,
            fillHue: rand(300, 330),
            fillL: rand(12, 45),
            fillA: rand(0.15, 0.5),
            strokeHue: rand(300, 340),
            strokeL: rand(60, 85),
            strokeA: rand(0.2, 0.75),
        };
    };

    let hearts = [];

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

    const wrap = (heart) => {
        const margin = 70;
        if (heart.x < -margin) heart.x = width + margin;
        if (heart.x > width + margin) heart.x = -margin;
        if (heart.y < -margin) heart.y = height + margin;
        if (heart.y > height + margin) heart.y = -margin;
    };

    const update = (dt) => {
        const dampV = pow(0.94, dt * 60);
        const dampR = pow(0.96, dt * 60);

        for (const heart of hearts) {
            heart.x += (heart.dx + heart.vx) * dt;
            heart.y += (heart.dy + heart.vy) * dt;
            heart.rot += (heart.drot + heart.vrot) * dt;

            heart.vx *= dampV;
            heart.vy *= dampV;
            heart.vrot *= dampR;

            if (enablePush && mouse.active) {
                const dx = heart.x - mouse.x;
                const dy = heart.y - mouse.y;
                const dist = Math.hypot(dx, dy);
                if (dist > 0.001 && dist < pushRadius) {
                    const t = 1 - dist / pushRadius;
                    const nx = dx / dist;
                    const ny = dy / dist;

                    const forcePerFrame = 2.5 * t;
                    heart.vx += nx * forcePerFrame * 60 * dt;
                    heart.vy += ny * forcePerFrame * 60 * dt;

                    const spinPerSec = rand(0.01, 0.03) * 60;
                    heart.vrot += heart.spinDir * spinPerSec * t * dt * 60;
                }
            }

            wrap(heart);
        }
    };

    const drawGlow = () => {
        if (!enablePush || !mouse.active) return;
        const gradient = ctx.createRadialGradient(mouse.x, mouse.y, 0, mouse.x, mouse.y, glowRadius);
        gradient.addColorStop(0, "rgba(220, 80, 180, 0.18)");
        gradient.addColorStop(1, "rgba(220, 80, 180, 0)");
        ctx.fillStyle = gradient;
        ctx.fillRect(mouse.x - glowRadius, mouse.y - glowRadius, glowRadius * 2, glowRadius * 2);
    };

    const drawHeartPath = (s) => {
        ctx.beginPath();
        ctx.moveTo(0, -s * 0.25);
        ctx.bezierCurveTo(s * 0.5, -s * 0.75, s, s * 0.1, 0, s * 0.6);
        ctx.bezierCurveTo(-s, s * 0.1, -s * 0.5, -s * 0.75, 0, -s * 0.25);
        ctx.closePath();
    };

    const drawHeart = (heart, ts) => {
        let proximity = 0;
        if (enablePush && mouse.active) {
            const dist = Math.hypot(heart.x - mouse.x, heart.y - mouse.y);
            if (dist < pushRadius) {
                proximity = 1 - dist / pushRadius;
            }
        }

        const fillL = clamp(heart.fillL + 35 * proximity, 0, 100);
        const strokeA = heart.strokeA + (0.8 - heart.strokeA) * proximity;

        const pulse = 1 + 0.2 * proximity * (0.6 + 0.4 * Math.sin(ts * 0.004 + heart.phase));
        const s = heart.size * pulse;

        ctx.save();
        ctx.translate(heart.x, heart.y);
        ctx.rotate(heart.rot);

        drawHeartPath(s);

        ctx.fillStyle = `hsla(${heart.fillHue.toFixed(0)}, 70%, ${fillL.toFixed(0)}%, ${heart.fillA.toFixed(2)})`;
        ctx.strokeStyle = `hsla(${heart.strokeHue.toFixed(0)}, 85%, ${heart.strokeL.toFixed(0)}%, ${strokeA.toFixed(2)})`;
        ctx.lineWidth = 1;
        ctx.fill();
        ctx.stroke();

        ctx.restore();
    };

    const draw = (ts) => {
        ctx.clearRect(0, 0, width, height);
        ctx.fillStyle = backgroundColor;
        ctx.fillRect(0, 0, width, height);

        if (enablePush && mouse.active) {
            ctx.save();
            ctx.globalCompositeOperation = "lighter";
            drawGlow();
            ctx.restore();
        }

        for (const heart of hearts) {
            drawHeart(heart, ts);
        }
    };

    const loop = (ts) => {
        const dt = clamp((ts - lastTs) / 1000, 0, 0.033);
        lastTs = ts;
        update(dt);
        draw(ts);
        rafId = window.requestAnimationFrame(loop);
    };

    const init = () => {
        resize();
        hearts = Array.from({ length: heartCount }, () => createHeart());

        draw(performance.now());
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
