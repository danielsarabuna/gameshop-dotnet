import React from 'react';

export const SkeletonCard: React.FC<{ count?: number }> = ({ count = 6 }) => {
  return (
    <>
      {Array.from({ length: count }).map((_, i) => (
        <div
          key={i}
          className="glass-card skeleton-card"
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '24px',
            minHeight: '360px',
            boxSizing: 'border-box',
          }}
        >
          <div style={{ width: '100%', display: 'flex', justifyContent: 'flex-end', height: '24px', marginBottom: '8px' }}>
            <div className="shimmer" style={{ width: '70px', height: '22px', borderRadius: '12px', background: 'rgba(255, 255, 255, 0.08)' }} />
          </div>

          <div
            className="shimmer"
            style={{
              width: '110px',
              height: '110px',
              borderRadius: '20px',
              marginBottom: '16px',
              background: 'rgba(255, 255, 255, 0.08)',
            }}
          />

          <div
            className="shimmer"
            style={{
              width: '75%',
              height: '22px',
              borderRadius: '8px',
              marginBottom: '12px',
              background: 'rgba(255, 255, 255, 0.08)',
            }}
          />

          <div
            className="shimmer"
            style={{
              width: '45%',
              height: '32px',
              borderRadius: '8px',
              marginBottom: '20px',
              background: 'rgba(255, 255, 255, 0.08)',
            }}
          />

          <div
            className="shimmer"
            style={{
              width: '100%',
              height: '46px',
              borderRadius: '20px',
              marginTop: 'auto',
              background: 'rgba(255, 255, 255, 0.08)',
            }}
          />
        </div>
      ))}
    </>
  );
};
