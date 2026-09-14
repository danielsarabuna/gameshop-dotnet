import React from 'react';
import { useLanguage } from '../context/LanguageContext';
import { HelpCircle, Mail, MessageSquare, UserCheck } from 'lucide-react';

export const Help: React.FC = () => {
  const { t } = useLanguage();

  const faqs = [
    {
      q: t('Где найти мой ID игрока?', 'Where can I find my Player ID?'),
      a: t('Ваш Player ID находится в настройках профиля внутри мобильного приложения GameShop.', 'Your Player ID is in the profile settings inside the GameShop mobile app.'),
    },
    {
      q: t('Как быстро зачисляются алмазы?', 'How quickly are diamonds credited?'),
      a: t('В 99% случаев зачисление происходит автоматически в течение 1–3 минут после подтверждения оплаты.', 'In 99% of cases, crediting happens automatically within 1-3 minutes.'),
    },
    {
      q: t('Что делать, если возникла ошибка оплаты?', 'What if a payment error occurs?'),
      a: t('Обратитесь в службу поддержки по email, указав номер заказа.', 'Contact support by email and include your order ID.'),
    },
  ];

  return (
    <div style={{ position: 'relative', width: '100%', minHeight: '100vh' }}>
      <img src="/images/story-bg.jpg" alt="" className="showcase-bg" />
      <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 1, pointerEvents: 'none' }} />
      <div className="page-content page-container" style={{ padding: '130px 6% 80px', maxWidth: 840, margin: '0 auto' }}>
          <div className="page-header seq-item seq-delay-1" style={{ textAlign: 'center', marginBottom: 40 }}>
            <h1 className="page-title" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, fontWeight: 800 }}>
              <HelpCircle size={36} color="var(--accent-pink)" />
              <span className="gradient-text">{t('Служба Помощи', 'Help & Support', 'Hilfe & Support', 'Aide et support', 'Ayuda y soporte')}</span>
            </h1>
            <p className="page-subtitle" style={{ fontSize: '1.05rem', margin: '12px auto 0' }}>
              {t('Ответы на популярные вопросы и связь со службой поддержки GameShop.', 'Frequently asked questions and contact with GameShop support.')}
            </p>
          </div>

          {/* HOW TO FIND PLAYER ID HIGHLIGHT CARD */}
          <div className="glass-card seq-item seq-delay-2" style={{ marginBottom: '32px', padding: '24px', border: '1px solid var(--accent-cyan)' }}>
            <h3 style={{ fontSize: '1.2rem', color: '#fff', marginBottom: '10px', display: 'flex', alignItems: 'center', gap: '10px' }}>
              <UserCheck size={22} color="var(--accent-cyan)" />
              {t('Как скопировать Player ID?', 'How to copy Player ID?')}
            </h3>
            <p style={{ fontSize: '0.95rem', color: 'var(--text-muted)', lineHeight: 1.6 }}>
              {t(
                'Откройте настройки игры GameShop -> раздел Профиль -> нажмите на кнопку Копировать ID. Вставьте полученный код в поле «ID игрока» в корзине.',
                'Open GameShop game settings -> Profile section -> tap Copy ID. Paste the resulting code into the Player ID field at checkout.'
              )}
            </p>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', marginBottom: '40px' }} className="seq-item seq-delay-3">
            {faqs.map((faq, idx) => (
              <div key={idx} className="glass-card" style={{ padding: '24px' }}>
                <h3 style={{ fontSize: '1.15rem', color: '#fff', marginBottom: '10px', display: 'flex', alignItems: 'center', gap: '10px' }}>
                  <HelpCircle size={20} color="var(--accent-pink)" />
                  {faq.q}
                </h3>
                <p style={{ fontSize: '0.95rem', color: 'var(--text-muted)', lineHeight: 1.6 }}>{faq.a}</p>
              </div>
            ))}
          </div>

          <div className="glass-card seq-item seq-delay-4" style={{ textAlign: 'center', padding: '36px' }}>
            <h2 style={{ fontSize: '1.4rem', color: '#fff', marginBottom: '12px', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '10px' }}>
              <MessageSquare size={24} color="var(--accent-pink)" />
              {t('Остались вопросы?', 'Still have questions?', 'Noch Fragen?', 'Des questions ?', '¿Tienes preguntas?')}
            </h2>
            <p className="muted" style={{ marginBottom: '24px' }}>
              {t('Свяжитесь с нами напрямую, мы на связи 24/7.', 'Contact us directly, we are available 24/7.')}
            </p>
            <div style={{ display: 'flex', gap: '16px', justifyContent: 'center', flexWrap: 'wrap' }}>
              <a href="https://github.com/danielsarabuna/web-shop/issues" target="_blank" rel="noreferrer" className="btn btn-primary">
                <MessageSquare size={18} />
                GitHub Issues
              </a>
              <a href="mailto:support@example.invalid" className="btn btn-outline">
                <Mail size={18} />
                Email Support
              </a>
            </div>
          </div>
      </div>
    </div>
  );
};
